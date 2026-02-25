namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Represents the outcome of an execution throttle evaluation.
/// </summary>
public sealed record ThrottleDecision
{
    public bool    Allowed           { get; }
    public string? ReasonCode        { get; }
    public string? Message           { get; }
    public int?    RetryAfterSeconds { get; }

    private ThrottleDecision(bool allowed, string? reasonCode, string? message, int? retryAfterSeconds)
    {
        Allowed           = allowed;
        ReasonCode        = reasonCode;
        Message           = message;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>Returns a decision that allows the execution to proceed.</summary>
    public static ThrottleDecision Allow()
        => new(true, null, null, null);

    /// <summary>Returns a decision that denies the execution due to throttling.</summary>
    public static ThrottleDecision Deny(int retryAfterSeconds)
        => new(false, "TooManyRequests",
               $"Execution throttled. Retry after {retryAfterSeconds} seconds.",
               retryAfterSeconds);
}
