using OpsCopilot.BuildingBlocks.Contracts.SafeActions;

namespace OpsCopilot.SafeActions.Application.Orchestration;

/// <summary>
/// Thin adapter that bridges <see cref="SafeActionOrchestrator"/> to the cross-module
/// <see cref="ISafeActionProposalService"/> contract. Maps domain entities and exceptions
/// to contract-level types so that consuming modules (e.g. Packs) never take a direct
/// dependency on SafeActions internals.
/// </summary>
internal sealed class SafeActionProposalServiceAdapter : ISafeActionProposalService
{
    private readonly SafeActionOrchestrator _orchestrator;

    public SafeActionProposalServiceAdapter(SafeActionOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task<SafeActionProposalResponse> ProposeAsync(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson,
        string? manualRollbackGuidance,
        CancellationToken ct = default)
    {
        try
        {
            var record = await _orchestrator.ProposeAsync(
                tenantId, runId, actionType, proposedPayloadJson,
                rollbackPayloadJson, manualRollbackGuidance, ct);

            return new SafeActionProposalResponse(
                record.ActionRecordId,
                record.RollbackStatus.ToString());
        }
        catch (PolicyDeniedException ex)
        {
            throw new SafeActionProposalDeniedException(ex.ReasonCode, ex.Message);
        }
    }
}
