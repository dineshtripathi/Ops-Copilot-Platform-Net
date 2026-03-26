namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Request body for POST /agent/chat.</summary>
/// <param name="Query">The operator's question or search string. Must not be empty.</param>
public sealed record ChatRequest(string Query);
