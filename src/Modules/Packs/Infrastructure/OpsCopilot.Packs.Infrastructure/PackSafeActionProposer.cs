using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Mode-B+ safe-action proposer. Discovers packs whose <c>MinimumMode</c> is at or below
/// the deployment mode, reads their action definition files, and returns them as
/// <em>proposals</em> (recommendations only — no execution or approval).
/// </summary>
/// <remarks>
/// Double-gated: proposal requires
/// <list type="number">
///   <item><c>deploymentMode &gt;= B</c></item>
///   <item><c>Packs:SafeActionsEnabled == true</c> in configuration</item>
/// </list>
/// All operations are read-only; no mutations are performed.
/// </remarks>
internal sealed class PackSafeActionProposer : IPackSafeActionProposer
{
    private readonly IPackCatalog    _catalog;
    private readonly IPackFileReader _fileReader;
    private readonly IConfiguration  _configuration;
    private readonly ILogger<PackSafeActionProposer> _logger;
    private readonly IPacksTelemetry _telemetry;

    public PackSafeActionProposer(
        IPackCatalog catalog,
        IPackFileReader fileReader,
        IConfiguration configuration,
        ILogger<PackSafeActionProposer> logger,
        IPacksTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fileReader);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(telemetry);

        _catalog       = catalog;
        _fileReader    = fileReader;
        _configuration = configuration;
        _logger        = logger;
        _telemetry     = telemetry;
    }

    /// <inheritdoc />
    public async Task<PackSafeActionProposalResult> ProposeAsync(
        PackSafeActionProposalRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var tenantId      = request.TenantId ?? "unknown";
        var deploymentMode = request.DeploymentMode;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"]  = correlationId,
            ["TenantId"]       = tenantId,
            ["DeploymentMode"] = deploymentMode
        });

        // ── Gate 1: deployment mode must be B or higher ──────────────────
        if (!IsModeEligible(deploymentMode))
        {
            _logger.LogDebug(
                "SafeAction proposal skipped — deployment mode {Mode} is below B",
                deploymentMode);

            _telemetry.RecordEvidenceSkipped(deploymentMode, tenantId);
            return new PackSafeActionProposalResult([], []);
        }

        // ── Gate 2: feature flag must be enabled ─────────────────────────
        if (!IsFeatureEnabled())
        {
            _logger.LogDebug("SafeAction proposal skipped — Packs:SafeActionsEnabled is false");

            _telemetry.RecordEvidenceSkipped(deploymentMode, tenantId);
            return new PackSafeActionProposalResult([], []);
        }

        // ── Discovery ────────────────────────────────────────────────────
        _telemetry.RecordEvidenceAttempt(deploymentMode, tenantId, correlationId);

        IReadOnlyList<LoadedPack> allPacks;
        try
        {
            allPacks = await _catalog.GetAllAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pack catalog failed during safe-action proposal");
            return new PackSafeActionProposalResult([], [$"Pack catalog error: {ex.Message}"]);
        }

        var eligiblePacks = allPacks
            .Where(p => p.Validation.IsValid
                        && IsModeAtOrBelow(p.Manifest.MinimumMode, deploymentMode))
            .ToList();

        var proposals = new List<PackSafeActionProposalItem>();
        var errors    = new List<string>();

        foreach (var pack in eligiblePacks)
        {
            await DiscoverPackActionsAsync(
                pack, deploymentMode, correlationId, tenantId,
                proposals, errors, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "SafeAction proposal complete: {ProposalCount} proposals, {ErrorCount} errors",
            proposals.Count, errors.Count);

        return new PackSafeActionProposalResult(proposals, errors);
    }

    // ── Per-pack action discovery ────────────────────────────────────────

    private async Task DiscoverPackActionsAsync(
        LoadedPack pack,
        string deploymentMode,
        string correlationId,
        string tenantId,
        List<PackSafeActionProposalItem> proposals,
        List<string> errors,
        CancellationToken ct)
    {
        foreach (var action in pack.Manifest.SafeActions)
        {
            // Per-action eligibility check — actions are always included as recommendations;
            // IsExecutableNow indicates whether the current mode can actually run them.
            bool isExecutableNow = IsModeAtOrBelow(action.RequiresMode, deploymentMode);
            string? executionBlockedReason = isExecutableNow ? null : "requires_higher_mode";

            if (!isExecutableNow)
            {
                _logger.LogDebug(
                    "Action {ActionId} in pack {PackName} requires mode {RequiresMode}, deployment is {DeploymentMode} — recommending with IsExecutableNow=false",
                    action.Id, pack.Manifest.Name, action.RequiresMode, deploymentMode);
            }

            try
            {
                var item = await BuildProposalItemAsync(
                    pack, action, isExecutableNow, executionBlockedReason, ct).ConfigureAwait(false);

                proposals.Add(item);

                _telemetry.RecordCollectorSuccess(
                    pack.Manifest.Name, action.Id, tenantId, correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to read action definition for {ActionId} in pack {PackName}",
                    action.Id, pack.Manifest.Name);

                proposals.Add(new PackSafeActionProposalItem(
                    PackName:              pack.Manifest.Name,
                    ActionId:              action.Id,
                    DisplayName:           action.Id,
                    ActionType:            "unknown",
                    RequiresMode:          action.RequiresMode,
                    DefinitionFile:        action.DefinitionFile,
                    ParametersJson:        null,
                    ErrorMessage:          ex.Message,
                    IsExecutableNow:       isExecutableNow,
                    ExecutionBlockedReason: executionBlockedReason));

                errors.Add($"Pack '{pack.Manifest.Name}' action '{action.Id}': {ex.Message}");

                _telemetry.RecordCollectorFailure(
                    pack.Manifest.Name, action.Id, tenantId, "DefinitionReadError", correlationId);
            }
        }
    }

    private async Task<PackSafeActionProposalItem> BuildProposalItemAsync(
        LoadedPack pack,
        PackSafeAction action,
        bool isExecutableNow,
        string? executionBlockedReason,
        CancellationToken ct)
    {
        string displayName = action.Id;
        string actionType  = "unknown";
        string? parametersJson = null;

        if (!string.IsNullOrWhiteSpace(action.DefinitionFile))
        {
            var json = await _fileReader.ReadFileAsync(
                pack.PackPath, action.DefinitionFile, ct).ConfigureAwait(false);

            if (json is not null)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("displayName", out var dn))
                    displayName = dn.GetString() ?? action.Id;

                if (root.TryGetProperty("actionType", out var at))
                    actionType = at.GetString() ?? "unknown";

                if (root.TryGetProperty("parameters", out var p))
                    parametersJson = p.GetRawText();
            }
        }

        return new PackSafeActionProposalItem(
            PackName:              pack.Manifest.Name,
            ActionId:              action.Id,
            DisplayName:           displayName,
            ActionType:            actionType,
            RequiresMode:          action.RequiresMode,
            DefinitionFile:        action.DefinitionFile,
            ParametersJson:        parametersJson,
            ErrorMessage:          null,
            IsExecutableNow:       isExecutableNow,
            ExecutionBlockedReason: executionBlockedReason);
    }

    // ── Static helpers (same pattern as PackEvidenceExecutor) ────────────

    internal static bool IsModeEligible(string mode) =>
        !string.IsNullOrWhiteSpace(mode)
        && char.ToUpperInvariant(mode[0]) >= 'B';

    internal static bool IsModeAtOrBelow(string packMode, string deploymentMode) =>
        !string.IsNullOrWhiteSpace(packMode)
        && !string.IsNullOrWhiteSpace(deploymentMode)
        && char.ToUpperInvariant(packMode[0]) <= char.ToUpperInvariant(deploymentMode[0]);

    private bool IsFeatureEnabled()
    {
        var value = _configuration["Packs:SafeActionsEnabled"];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
