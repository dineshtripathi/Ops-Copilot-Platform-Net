namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Result of a target-scope allowlist evaluation.
/// </summary>
public sealed record TargetScopeDecision(bool Allowed, string ReasonCode, string Message)
{
    public static TargetScopeDecision Allow()
        => new(true, "ALLOWED", "Scope check passed.");

    public static TargetScopeDecision Deny(string reasonCode, string message)
        => new(false, reasonCode, message);
}
