namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Classifies connectors into well-known integration categories.
/// </summary>
public enum ConnectorKind
{
    /// <summary>Read-only telemetry / monitoring data.</summary>
    Observability,

    /// <summary>Runbook content retrieval and search.</summary>
    Runbook,

    /// <summary>Action target metadata for safe-action execution.</summary>
    ActionTarget
}
