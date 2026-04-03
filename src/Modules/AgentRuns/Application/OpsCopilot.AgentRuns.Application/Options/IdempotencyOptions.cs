namespace OpsCopilot.AgentRuns.Application.Options;

/// <summary>
/// Controls triage-run deduplication behaviour.
/// Bound from the <c>AgentRun:Idempotency</c> configuration section.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "AgentRun:Idempotency";

    /// <summary>
    /// How long (in minutes) a completed triage run blocks re-runs for the same
    /// alert fingerprint within the same tenant.
    /// Set to 0 to disable deduplication entirely.
    /// Default: 60 minutes.
    /// </summary>
    public int WindowMinutes { get; set; } = 60;
}
