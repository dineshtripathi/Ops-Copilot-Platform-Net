namespace OpsCopilot.BuildingBlocks.Contracts.SafeActions;

/// <summary>
/// Cross-module contract that allows Packs (or any other module) to propose a
/// safe action record without referencing SafeActions.Application directly.
/// <para>
/// Implementations live in SafeActions.Application; consumers resolve this
/// interface via <c>IServiceScopeFactory</c> to honour scoped lifetimes.
/// </para>
/// </summary>
public interface ISafeActionProposalService
{
    /// <summary>
    /// Creates a new Proposed action record after evaluating all policy gates.
    /// </summary>
    /// <returns>A lightweight response containing the new record identifier and rollback status.</returns>
    /// <exception cref="SafeActionProposalDeniedException">
    /// Thrown when any governance / policy gate denies the proposal.
    /// </exception>
    Task<SafeActionProposalResponse> ProposeAsync(
        string  tenantId,
        Guid    runId,
        string  actionType,
        string  proposedPayloadJson,
        string? rollbackPayloadJson,
        string? manualRollbackGuidance,
        CancellationToken ct = default);
}
