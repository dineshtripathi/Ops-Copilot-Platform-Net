using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Shared configuration helpers for all OpsCopilot hosts (ApiHost, WorkerHost, McpHost).
///
/// Usage pattern for WebApplication hosts (ApiHost):
/// <code>
///   var builder = WebApplication.CreateBuilder(args);
///   // At this point appsettings.json + appsettings.{env}.json
///   // + User Secrets (Dev) + env vars are already loaded.
///
///   using var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
///   builder.Configuration.AddOpsCopilotKeyVault(
///       builder.Configuration["KeyVault:VaultUri"],
///       loggerFactory.CreateLogger("Startup"));
/// </code>
///
/// Usage pattern for generic hosts (WorkerHost, McpHost):
/// <code>
///   var builder = Host.CreateApplicationBuilder(args);
///   // User Secrets are NOT added automatically for generic hosts.
///   if (builder.Environment.IsDevelopment())
///       builder.Configuration.AddUserSecrets(Assembly.GetEntryAssembly()!);
///
///   using var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
///   builder.Configuration.AddOpsCopilotKeyVault(
///       builder.Configuration["KeyVault:VaultUri"],
///       loggerFactory.CreateLogger("Startup"));
/// </code>
/// </summary>
public static class HostConfigurationExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as a configuration source when
    /// <paramref name="vaultUri"/> is a non-empty, valid URI.
    ///
    /// If <paramref name="vaultUri"/> is null or whitespace (typical for local dev),
    /// the method returns without modifying the builder — startup never crashes.
    ///
    /// Key Vault secrets are reloaded every 30 minutes so that certificate rotations
    /// and secret updates are picked up without a restart.
    ///
    /// Authentication uses <see cref="DefaultAzureCredential"/>:
    ///   • Container Apps / App Service → system-assigned Managed Identity
    ///   • GitHub Actions              → federated OIDC credential
    ///   • Developer workstation       → <c>az login</c> / environment variables
    /// </summary>
    public static IConfigurationBuilder AddOpsCopilotKeyVault(
        this IConfigurationBuilder builder,
        string?                    vaultUri,
        ILogger?                   logger = null)
    {
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            logger?.LogInformation(
                "[Config] Azure Key Vault provider: DISABLED — " +
                "KeyVault:VaultUri is not set. " +
                "Use User Secrets or environment variables for local dev.");
            return builder;
        }

        if (!Uri.TryCreate(vaultUri, UriKind.Absolute, out var parsedUri))
        {
            logger?.LogWarning(
                "[Config] Azure Key Vault provider: DISABLED — " +
                "KeyVault:VaultUri '{VaultUri}' is not a valid URI. Check your configuration.",
                vaultUri);
            return builder;
        }

        logger?.LogInformation(
            "[Config] Azure Key Vault provider: ENABLED — vault={VaultUri}", parsedUri);

        builder.AddAzureKeyVault(
            parsedUri,
            new DefaultAzureCredential(),
            new AzureKeyVaultConfigurationOptions
            {
                // Reload secrets every 30 min — picks up rotations without restart.
                ReloadInterval = TimeSpan.FromMinutes(30),
            });

        return builder;
    }
}
