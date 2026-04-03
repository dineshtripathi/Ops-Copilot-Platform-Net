namespace OpsCopilot.AgentRuns.Application.Options;

/// <summary>
/// Controls the LLM multi-turn tool-call loop inside <see cref="OpsCopilot.AgentRuns.Application.Orchestration.TriageOrchestrator"/>.
/// Bound from the <c>AgentRun:AgentLoop</c> configuration section.
/// </summary>
public sealed class AgentLoopOptions
{
    public const string SectionName = "AgentRun:AgentLoop";

    /// <summary>
    /// Maximum number of tool-calling iterations the LLM loop may execute before
    /// returning the last available response. Applies only when <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// is registered. Default: 5.
    /// </summary>
    public int MaxToolCallIterations { get; init; } = 5;

    /// <summary>
    /// Maximum estimated token count allowed in the LLM context window before trimming.
    /// The <see cref="OpsCopilot.AgentRuns.Application.Abstractions.IContextWindowManager"/>
    /// trims the oldest non-system messages when this threshold is exceeded.
    /// Default: 100 000 tokens (≈ 400 000 characters using the 4-chars-per-token heuristic).
    /// Slice 200 — §3.15 LLM Context Window Management.
    /// </summary>
    public int ContextWindowBudgetTokens { get; init; } = 100_000;
}
