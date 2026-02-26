using System.Text.Json;
using OpsCopilot.BuildingBlocks.Contracts.AgentRuns;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Application.Services;

namespace OpsCopilot.AlertIngestion.Application.Handlers;

/// <summary>
/// Handles <see cref="IngestAlertCommand"/>:
///   1. Validates provider and payload.
///   2. Normalizes via <see cref="AlertNormalizerRouter"/>.
///   3. Computes deterministic fingerprint from normalized fields.
///   4. Creates a Pending AgentRun ledger entry via <see cref="IAgentRunCreator"/>.
///   5. Returns the new RunId and fingerprint to the caller.
/// </summary>
public sealed class IngestAlertCommandHandler
{
    private readonly IAgentRunCreator       _runCreator;
    private readonly AlertNormalizerRouter  _router;

    public IngestAlertCommandHandler(
        IAgentRunCreator      runCreator,
        AlertNormalizerRouter router)
    {
        _runCreator = runCreator;
        _router     = router;
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

        // Create ledger entry
        var runId = await _runCreator.CreateRunAsync(
            command.TenantId, fingerprint, sessionId: null, ct);

        return new IngestAlertResult(runId, fingerprint);
    }
}
