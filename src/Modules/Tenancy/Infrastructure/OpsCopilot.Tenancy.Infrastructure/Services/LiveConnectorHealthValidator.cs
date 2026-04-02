using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Tenancy.Application.Abstractions;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Slice 199 — validates all registered connectors via <see cref="IConnectorRegistry"/>
/// and <see cref="IConnectorHealthCheck"/>. Returns the names of unhealthy connectors.
/// Exceptions from individual checks are caught and treated as unhealthy (fail-safe).
/// Enabled via config key <c>Tenancy:Features:LiveConnectorHealthValidation = true</c>.
/// </summary>
internal sealed class LiveConnectorHealthValidator : IConnectorHealthValidator
{
    private readonly IConnectorRegistry _registry;
    private readonly IConnectorHealthCheck _healthCheck;
    private readonly ILogger<LiveConnectorHealthValidator> _logger;

    public LiveConnectorHealthValidator(
        IConnectorRegistry registry,
        IConnectorHealthCheck healthCheck,
        ILogger<LiveConnectorHealthValidator> logger)
    {
        _registry    = registry;
        _healthCheck = healthCheck;
        _logger      = logger;
    }

    public async Task<IReadOnlyList<string>> ValidateConnectorsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var connectors = _registry.ListAll();
        var unhealthy  = new List<string>(connectors.Count);
        var tenantStr  = tenantId.ToString();

        foreach (var descriptor in connectors)
        {
            try
            {
                var report = await _healthCheck.CheckAsync(tenantStr, descriptor.Name, ct);
                if (!report.IsHealthy)
                {
                    _logger.LogWarning(
                        "Connector '{ConnectorName}' is unhealthy for tenant {TenantId}: {Reason}",
                        descriptor.Name, tenantId, report.FailureReason);
                    unhealthy.Add(descriptor.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Health check threw for connector '{ConnectorName}', tenant {TenantId}; treating as unhealthy.",
                    descriptor.Name, tenantId);
                unhealthy.Add(descriptor.Name);
            }
        }

        return unhealthy;
    }
}
