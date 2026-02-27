namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Single connector entry within the connector-inventory report.
/// </summary>
public sealed record ConnectorInventoryRow(
    string Name,
    string Kind,
    string Description,
    IReadOnlyList<string> Capabilities);
