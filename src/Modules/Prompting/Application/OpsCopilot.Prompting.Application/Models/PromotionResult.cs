namespace OpsCopilot.Prompting.Application.Models;

/// <summary>
/// Outcome of evaluating a canary promotion gate.
/// </summary>
public enum PromotionResult
{
    /// <summary>No active canary experiment for the key.</summary>
    NoCanary,

    /// <summary>Quality score meets threshold — safe to promote.</summary>
    Promote,

    /// <summary>Quality score below threshold — reject candidate.</summary>
    Reject,
}
