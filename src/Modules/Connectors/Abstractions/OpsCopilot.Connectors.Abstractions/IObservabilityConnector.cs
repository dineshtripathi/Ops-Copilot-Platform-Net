namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Abstraction for read-only observability / telemetry data connectors
/// (e.g. Azure Monitor, Datadog, Prometheus).
/// </summary>
public interface IObservabilityConnector
{
    /// <summary>Metadata describing this connector.</summary>
    ConnectorDescriptor Descriptor { get; }

    /// <summary>Query types this connector can service (e.g. "log-query", "metric-query").</summary>
    IReadOnlyList<string> SupportedQueryTypes { get; }

    /// <summary>Returns <c>true</c> when this connector can handle the given query type.</summary>
    bool CanQuery(string queryType);
}
