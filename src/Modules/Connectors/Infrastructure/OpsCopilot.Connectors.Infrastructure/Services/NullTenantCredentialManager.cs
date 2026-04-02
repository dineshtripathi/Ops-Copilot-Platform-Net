using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Services;

/// <summary>
/// No-op implementation of <see cref="ITenantCredentialManager"/> used as the default
/// registration. Returns <c>null</c> for every secret and <see cref="RotationStatus.Unknown"/>
/// for all rotation-metadata queries.
/// </summary>
public sealed class NullTenantCredentialManager : ITenantCredentialManager
{
    /// <inheritdoc />
    public Task<string?> GetSecretAsync(
        string            tenantId,
        string            connectorName,
        CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<CredentialRotationMetadata> GetRotationMetadataAsync(
        string            tenantId,
        string            connectorName,
        CancellationToken ct = default)
        => Task.FromResult(
            new CredentialRotationMetadata(connectorName, null, null, RotationStatus.Unknown));
}
