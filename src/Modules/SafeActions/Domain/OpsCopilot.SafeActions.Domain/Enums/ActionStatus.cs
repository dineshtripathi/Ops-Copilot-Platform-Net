namespace OpsCopilot.SafeActions.Domain.Enums;

/// <summary>
/// Lifecycle status of an action record.
/// Transitions: Proposed → Approved → Executing → Completed | Failed
///              Proposed → Rejected (terminal)
/// </summary>
public enum ActionStatus
{
    Proposed,
    Approved,
    Rejected,
    Executing,
    Completed,
    Failed,
}
