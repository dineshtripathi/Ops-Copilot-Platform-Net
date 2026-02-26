using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// Azure Monitor / Log Analytics observability connector.
/// Thin capability-metadata wrapper — no actual Azure SDK calls yet.
/// </summary>
public sealed class AzureMonitorObservabilityConnector : IObservabilityConnector
{
    private static readonly string[] QueryTypes = ["log-query", "metric-query", "alert-read"];

    public ConnectorDescriptor Descriptor { get; } = new(
        "azure-monitor",
        ConnectorKind.Observability,
        "Azure Monitor / Log Analytics read-only telemetry connector",
        QueryTypes);

    public IReadOnlyList<string> SupportedQueryTypes => QueryTypes;

    public bool CanQuery(string queryType) =>
        SupportedQueryTypes.Contains(queryType, StringComparer.OrdinalIgnoreCase);
}
