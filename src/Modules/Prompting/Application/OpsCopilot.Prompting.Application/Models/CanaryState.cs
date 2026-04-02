namespace OpsCopilot.Prompting.Application.Models;

/// <summary>
/// Represents an active canary experiment for a prompt key.
/// Traffic is split between the current active template and the candidate.
/// </summary>
public sealed record CanaryState(
    string PromptKey,
    int    CandidateVersion,
    string CandidateContent,
    int    TrafficPercent,
    DateTimeOffset StartedAt);
