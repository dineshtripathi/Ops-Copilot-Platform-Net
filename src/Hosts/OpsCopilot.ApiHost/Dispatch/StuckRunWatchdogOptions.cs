namespace OpsCopilot.ApiHost.Dispatch;

/// <summary>
/// Slice 130: Options for <see cref="StuckRunWatchdog"/>.
/// Bind from configuration section <c>AgentRun:StuckRunWatchdog</c>.
/// </summary>
internal sealed class StuckRunWatchdogOptions
{
    /// <summary>
    /// How often the watchdog scans for stuck runs (seconds).
    /// Default: 300 s (5 minutes).
    /// </summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// A run in <c>Running</c> state whose <c>CreatedAtUtc</c> is older than this
    /// many minutes is considered stuck and will be driven to <c>Failed</c>.
    /// Default: 30 minutes — comfortably longer than the longest expected triage duration.
    /// </summary>
    public int ThresholdMinutes { get; set; } = 30;
}
