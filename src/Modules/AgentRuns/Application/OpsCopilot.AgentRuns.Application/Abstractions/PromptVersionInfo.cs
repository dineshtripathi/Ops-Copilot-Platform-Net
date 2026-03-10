namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>Versioned prompt template used by the triage LLM call.</summary>
public sealed record PromptVersionInfo(string VersionId, string SystemPrompt);
