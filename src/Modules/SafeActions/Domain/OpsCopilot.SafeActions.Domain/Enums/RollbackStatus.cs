namespace OpsCopilot.SafeActions.Domain.Enums;

/// <summary>
/// Lifecycle status of rollback for an action record.
/// Transitions: None (rollback not supported for this action type)
///              Available → Pending → Approved → RolledBack | RollbackFailed
///              ManualRequired (no auto-rollback; manual guidance stored)
/// </summary>
public enum RollbackStatus
{
    None,
    Available,
    Pending,
    Approved,
    RolledBack,
    RollbackFailed,
    ManualRequired,
}
