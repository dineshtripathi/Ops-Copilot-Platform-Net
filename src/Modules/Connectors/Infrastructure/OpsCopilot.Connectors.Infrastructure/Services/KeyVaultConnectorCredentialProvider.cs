using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Services;

/// <summary>
/// Resolves connector credentials from <see cref="IConfiguration"/>.
/// In production, IConfiguration is backed by the Azure Key Vault provider
/// registered in HostConfigurationExtensions.AddOpsCopilotKeyVault().
///
/// Secret naming convention: connector-{tenantId}-{connectorType}
/// where both segments are sanitised to contain only [a-zA-Z0-9\-].
/// </summary>
public sealed class KeyVaultConnectorCredentialProvider : IConnectorCredentialProvider
{
    private static readonly Regex _safePattern =
        new(@"[^a-zA-Z0-9\-]", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;

    public KeyVaultConnectorCredentialProvider(IConfiguration configuration)
        => _configuration = configuration;

    public string? GetSecret(string tenantId, string connectorType)
    {
        var safeTenantId = Sanitize(tenantId);
        var safeConnectorType = Sanitize(connectorType);
        var key = $"connector-{safeTenantId}-{safeConnectorType}";
        return _configuration[key];
    }

    private static string Sanitize(string value)
        => _safePattern.Replace(value, "-").Trim('-');
}
