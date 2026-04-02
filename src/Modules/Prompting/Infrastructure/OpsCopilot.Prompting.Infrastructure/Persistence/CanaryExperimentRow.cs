namespace OpsCopilot.Prompting.Infrastructure.Persistence;

/// <summary>
/// EF Core persistence row for an active canary experiment.
/// Maps to prompting.CanaryExperiments table.
/// </summary>
internal sealed class CanaryExperimentRow
{
    /// <summary>Natural PK — at most one active canary per prompt key.</summary>
    public string PromptKey { get; set; } = string.Empty;
    public int CandidateVersion { get; set; }
    public string CandidateContent { get; set; } = string.Empty;
    public int TrafficPercent { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
