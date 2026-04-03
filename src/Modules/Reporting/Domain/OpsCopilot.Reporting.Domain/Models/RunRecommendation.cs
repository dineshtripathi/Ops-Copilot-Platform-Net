namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 88: A deterministic operator next-step hint derived from briefing data.
/// No LLM involved. No raw evidence payloads. Safe for display on the detail page.
/// Key is a stable machine identifier; Instruction is the human-readable display text.
/// </summary>
public sealed record RunRecommendation(string Key, string Instruction);
