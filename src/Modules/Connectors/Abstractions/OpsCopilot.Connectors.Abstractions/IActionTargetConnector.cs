namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Abstraction for action-target metadata connectors that describe which
/// safe-action targets are reachable through this connector
/// (e.g. static in-memory list, Azure Resource Graph, ServiceNow CMDB).
/// </summary>
public interface IActionTargetConnector
{
    /// <summary>Metadata describing this connector.</summary>
    ConnectorDescriptor Descriptor { get; }

    /// <summary>Action types this connector can target (e.g. "restart-service", "scale-resource").</summary>
    IReadOnlyList<string> SupportedActionTypes { get; }

    /// <summary>Returns <c>true</c> when this connector supports the given action type.</summary>
    bool SupportsActionType(string actionType);
}
