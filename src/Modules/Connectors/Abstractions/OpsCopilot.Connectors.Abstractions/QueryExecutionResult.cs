namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Result of executing a single observability query via a connector.
/// </summary>
public sealed record QueryExecutionResult(
    bool Success,
    string? ResultJson,
    int RowCount,
    string? ErrorMessage,
    string[]? Columns = null,
    long DurationMs = 0,
    string? ErrorCode = null);
