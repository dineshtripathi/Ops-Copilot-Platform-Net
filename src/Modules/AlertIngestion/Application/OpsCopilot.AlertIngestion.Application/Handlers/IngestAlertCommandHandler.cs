using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Domain.Services;

namespace OpsCopilot.AlertIngestion.Application.Handlers;

/// <summary>
/// Handles <see cref="IngestAlertCommand"/>:
///   1. Computes a deterministic SHA-256 fingerprint for the payload.
///   2. Creates a Pending AgentRun ledger entry via <see cref="IAgentRunRepository"/>.
///   3. Returns the new RunId and fingerprint to the caller.
/// </summary>
public sealed class IngestAlertCommandHandler
{
    private readonly IAgentRunRepository _repository;

    public IngestAlertCommandHandler(IAgentRunRepository repository)
        => _repository = repository;

    public async Task<IngestAlertResult> HandleAsync(
        IngestAlertCommand command,
        CancellationToken  ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.TenantId))
            throw new ArgumentException(
                "TenantId must not be empty.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.RawJson))
            throw new ArgumentException(
                "RawJson must not be empty.", nameof(command));

        var fingerprint = AlertFingerprintService.Compute(command.RawJson);
        var run         = await _repository.CreateRunAsync(command.TenantId, fingerprint, sessionId: null, ct);

        return new IngestAlertResult(run.RunId, fingerprint);
    }
}
