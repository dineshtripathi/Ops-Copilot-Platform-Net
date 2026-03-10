namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Governs whether and when a failed safe-action proposal recording should be retried.
/// </summary>
public interface IProposalRecordingRetryPolicy
{
    /// <summary>Maximum number of retry attempts (not counting the initial call).</summary>
    int MaxAttempts { get; }

    /// <summary>
    /// Returns <c>true</c> when a further retry is permitted for the given (1-based) attempt number.
    /// </summary>
    bool ShouldRetry(int attemptNumber);

    /// <summary>
    /// Returns the delay to observe before executing the given (1-based) retry attempt.
    /// </summary>
    TimeSpan GetDelay(int attemptNumber);
}
