namespace OpsCopilot.AgentRuns.Presentation.Contracts;

// AG-UI typed streaming event records for POST /agent/triage/stream.
// Each record is serialized as a single SSE "data:" line with a "type" discriminator.

/// <summary>Emitted immediately when the SSE stream begins.</summary>
public sealed record RunStartedEvent(string Type, string RunId);

/// <summary>Emitted before the first text content chunk.</summary>
public sealed record TextMessageStartEvent(string Type, string MessageId, string Role);

/// <summary>One text chunk from the LLM narrative. Repeats until narrative is exhausted.</summary>
public sealed record TextMessageContentEvent(string Type, string MessageId, string Delta);

/// <summary>Emitted after the last text content chunk.</summary>
public sealed record TextMessageEndEvent(string Type, string MessageId);

/// <summary>Emitted when orchestration completes successfully. Carries the full triage response.</summary>
public sealed record RunFinishedEvent(string Type, string RunId, TriageResponse Response);

/// <summary>Emitted instead of RunFinished when orchestration throws a handled exception.</summary>
public sealed record RunErrorEvent(string Type, string RunId, string Message);
