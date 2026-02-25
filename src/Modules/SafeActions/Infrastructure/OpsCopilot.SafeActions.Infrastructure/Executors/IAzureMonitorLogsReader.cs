namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Abstraction over Azure Monitor / Log Analytics query execution.
/// Enables deterministic unit testing without hitting the real SDK.
/// </summary>
internal interface IAzureMonitorLogsReader
{
    /// <summary>
    /// Executes a read-only KQL query against a Log Analytics workspace.
    /// </summary>
    /// <param name="workspaceId">The Log Analytics workspace GUID.</param>
    /// <param name="query">The KQL query string.</param>
    /// <param name="timespan">The time window for the query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MonitorQueryResult"/> with serialised rows.</returns>
    Task<MonitorQueryResult> QueryLogsAsync(
        string workspaceId, string query, TimeSpan timespan, CancellationToken ct);
}

/// <summary>
/// Immutable result returned by <see cref="IAzureMonitorLogsReader"/>.
/// </summary>
/// <param name="RowCount">Number of rows returned.</param>
/// <param name="ColumnCount">Number of columns in the result table.</param>
/// <param name="ResultJson">Serialised JSON array of row objects.</param>
internal sealed record MonitorQueryResult(int RowCount, int ColumnCount, string ResultJson);
