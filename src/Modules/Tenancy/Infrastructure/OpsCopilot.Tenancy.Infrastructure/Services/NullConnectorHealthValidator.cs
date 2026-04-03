using OpsCopilot.Tenancy.Application.Abstractions;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Null implementation of <see cref="IConnectorHealthValidator"/>.
/// Always reports all connectors as healthy — safe default when no connector
/// integration is configured.
/// §6.19 — Onboarding Orchestration.
/// </summary>
public sealed class NullConnectorHealthValidator : IConnectorHealthValidator
{
    public Task<IReadOnlyList<string>> ValidateConnectorsAsync(
        Guid tenantId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
