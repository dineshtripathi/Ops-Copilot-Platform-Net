using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Default retry policy: up to 3 attempts with increasing delays (0 s, 1 s, 2 s).
/// </summary>
public sealed class DefaultProposalRecordingRetryPolicy : IProposalRecordingRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,              // attempt 1 — immediate
        TimeSpan.FromSeconds(1),    // attempt 2
        TimeSpan.FromSeconds(2),    // attempt 3
    ];

    /// <inheritdoc />
    public int MaxAttempts => 3;

    /// <inheritdoc />
    /// <param name="attemptNumber">1-based attempt number (1 = first try, 2 = first retry, …).</param>
    public bool ShouldRetry(int attemptNumber) => attemptNumber <= MaxAttempts;

    /// <inheritdoc />
    /// <param name="attemptNumber">1-based attempt number.</param>
    public TimeSpan GetDelay(int attemptNumber)
    {
        var index = Math.Clamp(attemptNumber - 1, 0, Delays.Length - 1);
        return Delays[index];
    }
}
