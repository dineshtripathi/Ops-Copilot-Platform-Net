namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Read-only inventory of all registered connectors with a breakdown by kind.
/// </summary>
public sealed record ConnectorInventoryReport(
    int TotalConnectors,
    IReadOnlyDictionary<string, int> ByKind,
    IReadOnlyList<ConnectorInventoryRow> Connectors,
    DateTime GeneratedAtUtc);
