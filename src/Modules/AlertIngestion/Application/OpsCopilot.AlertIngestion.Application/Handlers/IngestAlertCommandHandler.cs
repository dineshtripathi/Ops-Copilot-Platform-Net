using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.AgentRuns;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Application.Services;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Handlers;

/// <summary>
/// Handles <see cref="IngestAlertCommand"/>:
///   1. Validates provider and payload.
///   2. Normalizes via <see cref="AlertNormalizerRouter"/>.
///   3. Computes deterministic fingerprint from normalized fields.
///   4. Creates a Pending AgentRun ledger entry via <see cref="IAgentRunCreator"/>.
///   5. Attempts to dispatch via <see cref="IAlertTriageDispatcher"/> (graceful degradation).
///   6. Returns the new RunId, fingerprint, and dispatch status to the caller.
/// </summary>
public sealed class IngestAlertCommandHandler
{
    private readonly IAgentRunCreator        _runCreator;
    private readonly AlertNormalizerRouter   _router;
    private readonly IAlertTriageDispatcher  _dispatcher;
    private readonly ILogger<IngestAlertCommandHandler> _logger;
    private readonly ISessionPolicy          _sessionPolicy;

    public IngestAlertCommandHandler(
        IAgentRunCreator               runCreator,
        AlertNormalizerRouter          router,
        IAlertTriageDispatcher         dispatcher,
        ILogger<IngestAlertCommandHandler> logger,
        ISessionPolicy                 sessionPolicy)
    {
        _runCreator    = runCreator;
        _router        = router;
        _dispatcher    = dispatcher;
        _logger        = logger;
        _sessionPolicy = sessionPolicy;
    }

    public async Task<IngestAlertResult> HandleAsync(
        IngestAlertCommand command,
        CancellationToken  ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.TenantId))
            throw new ArgumentException(
                "TenantId must not be empty.", nameof(command));

        // Validate payload first
        var payloadValidation = AlertValidationService.ValidatePayload(command.RawJson);
        if (!payloadValidation.IsValid)
            throw new ArgumentException(payloadValidation.Message, nameof(command));

        // Validate provider
        var providerValidation = AlertValidationService.ValidateProvider(command.Provider, _router);
        if (!providerValidation.IsValid)
            throw new ArgumentException(providerValidation.Message, nameof(command));

        // Normalize
        var payload    = JsonDocument.Parse(command.RawJson).RootElement;
        var normalized = _router.Normalize(command.Provider, payload);

        // Fingerprint from normalized fields
        var fingerprint = NormalizedAlertFingerprintService.Compute(normalized);

        var context = BuildRunContext(normalized);

        // Slice 131: Resume existing session when this fingerprint was seen recently.
        var ttl          = _sessionPolicy.GetSessionTtl(command.TenantId);
        var windowMinutes = (int)ttl.TotalMinutes;
        var sessionId    = windowMinutes > 0
            ? await _runCreator.FindRecentSessionIdAsync(command.TenantId, fingerprint, windowMinutes, ct)
            : null;

        // Create ledger entry
        var runId = await _runCreator.CreateRunAsync(
            command.TenantId,
            fingerprint,
            sessionId: sessionId,
            context: context,
            ct: ct);

        var dispatched = false;
        try
        {
            dispatched = await _dispatcher.DispatchAsync(command.TenantId, runId, fingerprint, ct);
        }
        catch (Exception ex)
        {
            // Dispatch failure must not fail the ingestion — log and continue.
            _logger.LogWarning(ex,
                "Triage dispatch failed for run {RunId} (tenant={TenantId}). Ingestion accepted without dispatch.",
                runId, command.TenantId);
        }

        return new IngestAlertResult(runId, fingerprint, dispatched);
    }

    private static AlertRunContext BuildRunContext(NormalizedAlert normalized)
    {
        var resourceId = (string?)normalized.ResourceId;
        var (subscriptionId, resourceGroup) = ExtractArmScope(resourceId);

        var sourceType = ((string?)normalized.SourceType) ?? string.Empty;
        var title = ((string?)normalized.Title) ?? string.Empty;
        var description = ((string?)normalized.Description) ?? string.Empty;
        var isException =
            sourceType.Contains("application", StringComparison.OrdinalIgnoreCase)
            || sourceType.Contains("log", StringComparison.OrdinalIgnoreCase)
            || title.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || description.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || title.Contains("error", StringComparison.OrdinalIgnoreCase);

        var azureApplication = ExtractApplicationName(resourceId);
        var workspaceId = ExtractWorkspaceId(normalized.Dimensions as IReadOnlyDictionary<string, string>);

        return new AlertRunContext(
            AlertProvider: normalized.Provider,
            AlertSourceType: normalized.SourceType,
            IsExceptionSignal: isException,
            AzureSubscriptionId: subscriptionId,
            AzureResourceGroup: resourceGroup,
            AzureResourceId: resourceId,
            AzureApplication: azureApplication,
            AzureWorkspaceId: workspaceId);
    }

    private static (string? SubscriptionId, string? ResourceGroup) ExtractArmScope(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return (null, null);

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return (null, null);

        string? subscriptionId = null;
        string? resourceGroup = null;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
                subscriptionId = segments[i + 1];

            if (segments[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                resourceGroup = segments[i + 1];
        }

        return (subscriptionId, resourceGroup);
    }

    private static string? ExtractApplicationName(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return null;

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("components", StringComparison.OrdinalIgnoreCase))
                return segments[i + 1];
        }

        return null;
    }

    private static string? ExtractWorkspaceId(IReadOnlyDictionary<string, string>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
            return null;

        if (dimensions.TryGetValue("workspaceId", out var explicitWorkspaceId) &&
            !string.IsNullOrWhiteSpace(explicitWorkspaceId))
            return explicitWorkspaceId;

        return null;
    }
}
