namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Checks whether the credential for a named connector is present and usable.
/// </summary>
public interface IConnectorHealthCheck
{
    /// <summary>
    /// Returns a <see cref="ConnectorHealthReport"/> indicating whether the
    /// credential for <paramref name="connectorName"/> is configured for the
    /// given <paramref name="tenantId"/>.
    /// </summary>
    Task<ConnectorHealthReport> CheckAsync(
        string tenantId,
        string connectorName,
        CancellationToken ct = default);
}
