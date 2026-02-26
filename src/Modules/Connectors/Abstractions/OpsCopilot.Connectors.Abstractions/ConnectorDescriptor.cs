namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Immutable descriptor that every connector exposes so the registry
/// can catalogue and resolve connectors without touching implementation details.
/// </summary>
/// <param name="Name">
/// Unique, case-insensitive identifier used for DI resolution
/// (e.g. "azure-monitor", "in-memory-runbook").
/// </param>
/// <param name="Kind">The integration category this connector belongs to.</param>
/// <param name="Description">Human-readable one-liner for diagnostics / logging.</param>
/// <param name="Capabilities">
/// Flat list of capability tags the connector advertises
/// (e.g. "log-query", "metric-query"). Used for capability probing only —
/// no runtime dispatch is based on these today.
/// </param>
public sealed record ConnectorDescriptor(
    string Name,
    ConnectorKind Kind,
    string Description,
    IReadOnlyList<string> Capabilities);
