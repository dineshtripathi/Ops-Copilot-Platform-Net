namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Risk classification for an action type.
/// Used by the action-type catalog to communicate risk posture in API responses.
/// </summary>
public enum ActionRiskTier
{
    Low,
    Medium,
    High,
}
