namespace OpsCopilot.Governance.Application.Models;

/// <summary>
/// Result of a token-budget policy check.
/// </summary>
/// <param name="Allowed">Whether the run may proceed.</param>
/// <param name="ReasonCode">Machine-readable code (e.g. BUDGET_EXCEEDED, ALLOWED).</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="MaxTokens">Optional token cap for the run (null = unlimited).</param>
public sealed record BudgetDecision(bool Allowed, string ReasonCode, string Message, int? MaxTokens)
{
    public static BudgetDecision Allow(int? maxTokens = null)
        => new(true, "ALLOWED", "Budget check passed.", maxTokens);

    public static BudgetDecision Deny(string reasonCode, string message)
        => new(false, reasonCode, message, null);
}
