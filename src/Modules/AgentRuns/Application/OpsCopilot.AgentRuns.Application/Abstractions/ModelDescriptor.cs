namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>Identifies an LLM model and its optional endpoint override.</summary>
public sealed record ModelDescriptor(string ModelId, string? Endpoint = null);
