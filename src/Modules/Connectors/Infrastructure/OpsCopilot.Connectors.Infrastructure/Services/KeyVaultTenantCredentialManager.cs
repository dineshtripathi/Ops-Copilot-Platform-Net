using Azure.Security.KeyVault.Secrets;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Services;

/// <summary>
/// Azure Key Vault-backed implementation of <see cref="ITenantCredentialManager"/>.
/// Resolves secrets using the §6.18 canonical naming convention:
///   tenant-{tenantId}--connector-{connectorName}--credential
/// Rotation metadata is derived from the secret's expiry properties via
/// <see cref="CredentialRotationClassifier"/>.
/// Slice 182 — §6.18 Tenant credential Key Vault manager.
/// </summary>
internal sealed class KeyVaultTenantCredentialManager : ITenantCredentialManager
{
    private readonly Func<string, CancellationToken, Task<KeyVaultSecret?>> _fetch;

    /// <summary>Production constructor — wraps a real <see cref="SecretClient"/>.</summary>
    internal KeyVaultTenantCredentialManager(SecretClient client)
        : this(async (name, ct) =>
        {
            try
            {
                var response = await client.GetSecretAsync(name, version: null, ct);
                return response.Value;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        })
    {
    }

    /// <summary>Testability constructor — injected delegate replaces the remote call.</summary>
    internal KeyVaultTenantCredentialManager(Func<string, CancellationToken, Task<KeyVaultSecret?>> fetch)
        => _fetch = fetch;

    /// <inheritdoc/>
    public async Task<string?> GetSecretAsync(
        string tenantId, string connectorName, CancellationToken ct = default)
    {
        var secretName = ITenantCredentialManager.BuildSecretName(tenantId, connectorName);
        var secret = await _fetch(secretName, ct);
        return secret?.Value;
    }

    /// <inheritdoc/>
    public async Task<CredentialRotationMetadata> GetRotationMetadataAsync(
        string tenantId, string connectorName, CancellationToken ct = default)
    {
        var secretName = ITenantCredentialManager.BuildSecretName(tenantId, connectorName);
        var secret = await _fetch(secretName, ct);

        if (secret is null)
            return new CredentialRotationMetadata(connectorName, null, null, RotationStatus.Unknown);

        var expiresAt   = secret.Properties.ExpiresOn;
        var lastRotated = secret.Properties.UpdatedOn;
        var status      = CredentialRotationClassifier.Classify(expiresAt, DateTimeOffset.UtcNow);

        return new CredentialRotationMetadata(connectorName, lastRotated, expiresAt, status);
    }
}
