namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Provides per-tenant connector credentials resolved at runtime.
/// In production the implementation reads from IConfiguration which is
/// backed by the Azure Key Vault configuration provider.
/// </summary>
public interface IConnectorCredentialProvider
{
    /// <summary>
    /// Returns the secret value for the given tenant/connector combination,
    /// or <c>null</c> if no credential is configured.
    /// </summary>
    string? GetSecret(string tenantId, string connectorType);
}
