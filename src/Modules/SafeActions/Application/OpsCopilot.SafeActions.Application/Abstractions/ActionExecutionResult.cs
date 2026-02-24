namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Result of an action execution or rollback attempt.
/// </summary>
public sealed record ActionExecutionResult(
    bool   Success,
    string ResponseJson,
    long   DurationMs);
