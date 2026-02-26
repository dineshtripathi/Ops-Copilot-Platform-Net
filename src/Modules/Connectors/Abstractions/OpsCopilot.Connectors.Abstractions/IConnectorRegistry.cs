namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Central read-only catalogue that resolves registered connectors by name
/// and kind. Resolution is case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>).
/// </summary>
public interface IConnectorRegistry
{
    /// <summary>Look up an observability connector by its unique name, or <c>null</c> if not found.</summary>
    IObservabilityConnector? GetObservabilityConnector(string name);

    /// <summary>Look up a runbook connector by its unique name, or <c>null</c> if not found.</summary>
    IRunbookConnector? GetRunbookConnector(string name);

    /// <summary>Look up an action-target connector by its unique name, or <c>null</c> if not found.</summary>
    IActionTargetConnector? GetActionTargetConnector(string name);

    /// <summary>Returns descriptors for every registered connector across all kinds.</summary>
    IReadOnlyList<ConnectorDescriptor> ListAll();

    /// <summary>Returns descriptors for every registered connector of the specified kind.</summary>
    IReadOnlyList<ConnectorDescriptor> ListByKind(ConnectorKind kind);
}
