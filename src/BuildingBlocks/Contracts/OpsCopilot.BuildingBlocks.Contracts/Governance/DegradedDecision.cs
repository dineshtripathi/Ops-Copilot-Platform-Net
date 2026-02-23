namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Maps a runtime failure to a deterministic degraded-mode outcome.
/// </summary>
/// <param name="IsDegraded">True = degraded (partial data); False = failed (unrecoverable).</param>
/// <param name="ErrorCode">Machine-readable error classification (e.g. TOOL_TIMEOUT, AUTH_FAILURE).</param>
/// <param name="UserMessage">Safe message for API consumers (no stack traces).</param>
/// <param name="Retryable">Hint: can the caller retry this operation?</param>
public sealed record DegradedDecision(
    bool IsDegraded,
    string ErrorCode,
    string UserMessage,
    bool Retryable);
