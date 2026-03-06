namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Executes observability queries against a telemetry back-end
/// (e.g. Azure Monitor Log Analytics).
/// Deliberately separated from <see cref="IObservabilityConnector"/> which
/// is limited to capability / metadata concerns.
/// </summary>
public interface IObservabilityQueryExecutor
{
    /// <summary>
    /// Runs a query against the specified workspace and returns the result.
    /// </summary>
    /// <param name="workspaceId">Log Analytics workspace GUID.</param>
    /// <param name="queryText">KQL query text.</param>
    /// <param name="timespan">
    /// Optional time range. When <c>null</c> the implementation should default
    /// to a sensible window (e.g. 60 minutes).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<QueryExecutionResult> ExecuteQueryAsync(
        string workspaceId,
        string queryText,
        TimeSpan? timespan,
        CancellationToken ct = default);
}
