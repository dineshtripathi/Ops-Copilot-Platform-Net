namespace OpsCopilot.BuildingBlocks.Contracts.SafeActions;

/// <summary>
/// Lightweight cross-module response returned by <see cref="ISafeActionProposalService"/>
/// after a successful proposal record creation. Carries only the fields that the
/// calling module (Packs) needs — avoids leaking the SafeActions domain entity.
/// </summary>
/// <param name="ActionRecordId">The unique identifier of the newly-created action record.</param>
/// <param name="RollbackStatus">Rollback availability: "Available", "ManualRequired", or "None".</param>
public sealed record SafeActionProposalResponse(
    Guid   ActionRecordId,
    string RollbackStatus);
