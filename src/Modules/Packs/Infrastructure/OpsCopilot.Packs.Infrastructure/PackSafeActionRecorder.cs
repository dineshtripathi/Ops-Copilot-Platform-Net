using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.BuildingBlocks.Contracts.SafeActions;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Mode-C only safe-action recorder. Takes pack safe-action proposals that were already
/// discovered by <see cref="PackSafeActionProposer"/> and forwards eligible ones to the
/// SafeActions module via <see cref="ISafeActionProposalService"/> to create persisted
/// <c>ActionRecord</c> entries with <c>Status = Proposed</c>.
/// </summary>
/// <remarks>
/// Double-gated: recording requires
/// <list type="number">
///   <item><c>deploymentMode == C</c> (strict equality, not &gt;=)</item>
///   <item><c>Packs:SafeActionsEnabled == true</c> in configuration</item>
/// </list>
/// All proposals are recommend-only; no auto-approve or auto-execute occurs.
/// </remarks>
internal sealed class PackSafeActionRecorder : IPackSafeActionRecorder
{
    private readonly IConfiguration  _configuration;
    private readonly ILogger<PackSafeActionRecorder> _logger;
    private readonly IPacksTelemetry _telemetry;
    private readonly IServiceScopeFactory _scopeFactory;

    public PackSafeActionRecorder(
        IConfiguration configuration,
        ILogger<PackSafeActionRecorder> logger,
        IPacksTelemetry telemetry,
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _configuration = configuration;
        _logger        = logger;
        _telemetry     = telemetry;
        _scopeFactory  = scopeFactory;
    }

    public async Task<PackSafeActionRecordResult> RecordAsync(
        PackSafeActionRecordRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mode     = request.DeploymentMode;
        var tenantId = request.TenantId;

        // Gate 1: strict Mode C only (not >= B like the proposer).
        if (!IsModeC(mode))
        {
            _telemetry.RecordSafeActionSkipped("gate", "mode_not_c", tenantId, null);
            _logger.LogDebug("SafeAction recording skipped — mode {Mode} is not C", mode);
            return EmptyResult();
        }

        // Gate 2: feature flag.
        if (!IsFeatureEnabled())
        {
            _telemetry.RecordSafeActionSkipped("gate", "feature_disabled", tenantId, null);
            _logger.LogDebug("SafeAction recording skipped — Packs:SafeActionsEnabled is not true");
            return EmptyResult();
        }

        var correlationId = tenantId; // simple correlation; aligns with proposer pattern
        _telemetry.RecordSafeActionAttempt(mode, tenantId, correlationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["DeploymentMode"] = mode,
            ["TenantId"]       = tenantId,
            ["TriageRunId"]    = request.TriageRunId
        });

        var records = new List<PackSafeActionRecordItem>(request.Proposals.Count);
        var errors  = new List<string>();
        int created = 0, skipped = 0, failed = 0;

        foreach (var proposal in request.Proposals)
        {
            ct.ThrowIfCancellationRequested();

            // Skip proposals that are not executable or governance-denied at proposal time.
            if (!proposal.IsExecutableNow || proposal.GovernanceAllowed == false)
            {
                var skipReason = !proposal.IsExecutableNow
                    ? "not_executable"
                    : "governance_denied";

                _telemetry.RecordSafeActionSkipped(
                    proposal.PackName, proposal.ActionId, tenantId, skipReason);

                records.Add(new PackSafeActionRecordItem(
                    proposal.PackName,
                    proposal.ActionId,
                    proposal.ActionType,
                    ActionRecordId: null,
                    Status: "Skipped",
                    ErrorMessage: proposal.ErrorMessage ?? proposal.ExecutionBlockedReason,
                    PolicyDenialReasonCode: null));

                skipped++;
                continue;
            }

            try
            {
                // Resolve ISafeActionProposalService from a new scope (scoped lifetime).
                using var serviceScope    = _scopeFactory.CreateScope();
                var proposalService = serviceScope.ServiceProvider
                    .GetRequiredService<ISafeActionProposalService>();

                var response = await proposalService.ProposeAsync(
                    tenantId,
                    request.TriageRunId,
                    proposal.ActionType,
                    proposal.ParametersJson ?? "{}",
                    rollbackPayloadJson: null,
                    manualRollbackGuidance: null,
                    ct);

                _telemetry.RecordSafeActionCreated(
                    proposal.PackName, proposal.ActionId, tenantId, correlationId);

                records.Add(new PackSafeActionRecordItem(
                    proposal.PackName,
                    proposal.ActionId,
                    proposal.ActionType,
                    ActionRecordId: response.ActionRecordId,
                    Status: "Created",
                    ErrorMessage: null,
                    PolicyDenialReasonCode: null));

                created++;
            }
            catch (SafeActionProposalDeniedException denied)
            {
                _telemetry.RecordSafeActionDenied(
                    proposal.PackName, proposal.ActionId, tenantId,
                    denied.ReasonCode, correlationId);

                records.Add(new PackSafeActionRecordItem(
                    proposal.PackName,
                    proposal.ActionId,
                    proposal.ActionType,
                    ActionRecordId: null,
                    Status: "PolicyDenied",
                    ErrorMessage: denied.Message,
                    PolicyDenialReasonCode: denied.ReasonCode));

                failed++;
                errors.Add($"[{proposal.PackName}/{proposal.ActionId}] PolicyDenied: {denied.ReasonCode}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SafeAction recording failed for {PackName}/{ActionId}",
                    proposal.PackName, proposal.ActionId);

                _telemetry.RecordSafeActionFailed(
                    proposal.PackName, proposal.ActionId, tenantId,
                    ex.GetType().Name, correlationId);

                records.Add(new PackSafeActionRecordItem(
                    proposal.PackName,
                    proposal.ActionId,
                    proposal.ActionType,
                    ActionRecordId: null,
                    Status: "Failed",
                    ErrorMessage: ex.Message,
                    PolicyDenialReasonCode: null));

                failed++;
                errors.Add($"[{proposal.PackName}/{proposal.ActionId}] Failed: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "SafeAction recording complete — Created={Created}, Skipped={Skipped}, Failed={Failed}",
            created, skipped, failed);

        return new PackSafeActionRecordResult(records, created, skipped, failed, errors);
    }

    // ── Private helpers ────────────────────────────────────────

    /// <summary>Strict Mode C check — not &gt;= B like the proposer.</summary>
    private static bool IsModeC(string mode) =>
        mode.Length > 0 && char.ToUpperInvariant(mode[0]) == 'C';

    /// <summary>Feature flag: Packs:SafeActionsEnabled must be exactly "true".</summary>
    private bool IsFeatureEnabled() =>
        string.Equals(
            _configuration["Packs:SafeActionsEnabled"],
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static PackSafeActionRecordResult EmptyResult() =>
        new(Array.Empty<PackSafeActionRecordItem>(), 0, 0, 0, Array.Empty<string>());
}
