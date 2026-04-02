using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Services;

/// <summary>
/// Checks whether a connector credential is present by attempting to retrieve it
/// from the configured <see cref="IConnectorCredentialProvider"/>.
/// </summary>
public sealed class ConnectorHealthCheckRunner : IConnectorHealthCheck
{
    private readonly IConnectorCredentialProvider          _credentialProvider;
    private readonly ILogger<ConnectorHealthCheckRunner>   _logger;

    public ConnectorHealthCheckRunner(
        IConnectorCredentialProvider        credentialProvider,
        ILogger<ConnectorHealthCheckRunner> logger)
    {
        _credentialProvider = credentialProvider;
        _logger             = logger;
    }

    /// <inheritdoc />
    public Task<ConnectorHealthReport> CheckAsync(
        string            tenantId,
        string            connectorName,
        CancellationToken ct = default)
    {
        var secret    = _credentialProvider.GetSecret(tenantId, connectorName);
        var isHealthy = !string.IsNullOrWhiteSpace(secret);

        string? failureReason = null;
        if (!isHealthy)
        {
            failureReason = $"No credential configured for connector '{connectorName}' on tenant '{tenantId}'.";
            _logger.LogWarning(
                "Connector health check failed for tenant '{TenantId}', connector '{ConnectorName}': credential not found.",
                tenantId,
                connectorName);
        }

        return Task.FromResult(new ConnectorHealthReport(
            ConnectorName: connectorName,
            IsHealthy:     isHealthy,
            CheckedAt:     DateTimeOffset.UtcNow,
            FailureReason: failureReason));
    }
}
