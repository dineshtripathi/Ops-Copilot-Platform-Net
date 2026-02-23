namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Result of a tool-allowlist or generic policy check.
/// </summary>
/// <param name="Allowed">Whether the action is permitted.</param>
/// <param name="ReasonCode">Machine-readable code (e.g. TOOL_DENIED, ALLOWED).</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record PolicyDecision(bool Allowed, string ReasonCode, string Message)
{
    public static PolicyDecision Allow()
        => new(true, "ALLOWED", "Policy check passed.");

    public static PolicyDecision Deny(string reasonCode, string message)
        => new(false, reasonCode, message);
}
