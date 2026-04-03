namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Manages tenant-scoped connector credentials backed by Azure Key Vault.
///
/// Secret naming convention (§6.18):
///   <c>tenant-{tenantId}--connector-{connectorName}--credential</c>
///
/// Responsibilities:
///   - Resolve secrets using the canonical Key Vault namespace.
///   - Return rotation metadata including expiry and <see cref="RotationStatus"/>
///     so callers can surface proactive expiry warnings.
/// </summary>
public interface ITenantCredentialManager
{
    /// <summary>
    /// Returns the secret value for the given tenant/connector combination,
    /// or <c>null</c> if no credential is configured.
    /// The Key Vault secret name is derived via the canonical §6.18 naming scheme.
    /// </summary>
    Task<string?> GetSecretAsync(
        string            tenantId,
        string            connectorName,
        CancellationToken ct = default);

    /// <summary>
    /// Returns rotation metadata (last-rotated, expiry, status) for the credential.
    /// Fields are <c>null</c> when the underlying provider cannot expose expiry information.
    /// </summary>
    Task<CredentialRotationMetadata> GetRotationMetadataAsync(
        string            tenantId,
        string            connectorName,
        CancellationToken ct = default);

    /// <summary>
    /// Builds the canonical Key Vault secret name for a tenant/connector pair.
    /// Both segments are sanitised to <c>[a-zA-Z0-9\-]</c>.
    /// </summary>
    static string BuildSecretName(string tenantId, string connectorName)
    {
        var safeTenantId       = Sanitize(tenantId);
        var safeConnectorName  = Sanitize(connectorName);
        return $"tenant-{safeTenantId}--connector-{safeConnectorName}--credential";
    }

    private static string Sanitize(string value)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(value, @"[^a-zA-Z0-9\-]", "-");
        return sanitized.Trim('-');
    }
}
