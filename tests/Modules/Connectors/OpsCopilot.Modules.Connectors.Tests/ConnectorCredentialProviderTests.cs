using Microsoft.Extensions.Configuration;
using OpsCopilot.Connectors.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Connectors.Tests;

public sealed class ConnectorCredentialProviderTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void GetSecret_ConfiguredKey_ReturnsSecret()
    {
        var config = BuildConfig(new() { ["connector-tenant1-azuremonitor"] = "my-secret-value" });
        var sut = new KeyVaultConnectorCredentialProvider(config);

        var result = sut.GetSecret("tenant1", "azuremonitor");

        Assert.Equal("my-secret-value", result);
    }

    [Fact]
    public void GetSecret_UnconfiguredKey_ReturnsNull()
    {
        var config = BuildConfig(new());
        var sut = new KeyVaultConnectorCredentialProvider(config);

        var result = sut.GetSecret("tenant1", "azuremonitor");

        Assert.Null(result);
    }

    [Fact]
    public void GetSecret_TenantIdWithSpecialChars_Sanitized()
    {
        // tenant_id! becomes tenant-id- → trimmed dashes → tenant-id
        var config = BuildConfig(new() { ["connector-tenant-id-azuremonitor"] = "secret-a" });
        var sut = new KeyVaultConnectorCredentialProvider(config);

        var result = sut.GetSecret("tenant_id!", "azuremonitor");

        Assert.Equal("secret-a", result);
    }

    [Fact]
    public void GetSecret_ConnectorTypeWithUnderscores_Sanitized()
    {
        // azure_monitor becomes azure-monitor
        var config = BuildConfig(new() { ["connector-tenant1-azure-monitor"] = "secret-b" });
        var sut = new KeyVaultConnectorCredentialProvider(config);

        var result = sut.GetSecret("tenant1", "azure_monitor");

        Assert.Equal("secret-b", result);
    }

    [Fact]
    public void GetSecret_TenantIdWithLeadingTrailingSpecialChars_DashTrimmed()
    {
        // !tenant1! → after replace = -tenant1- → trimmed = tenant1
        var config = BuildConfig(new() { ["connector-tenant1-azuremonitor"] = "secret-c" });
        var sut = new KeyVaultConnectorCredentialProvider(config);

        var result = sut.GetSecret("!tenant1!", "azuremonitor");

        Assert.Equal("secret-c", result);
    }

    [Fact]
    public void GetSecret_DifferentTenants_AreIsolated()
    {
        var config = BuildConfig(new()
        {
            ["connector-tenanta-azuremonitor"] = "secret-a",
            ["connector-tenantb-azuremonitor"] = "secret-b"
        });
        var sut = new KeyVaultConnectorCredentialProvider(config);

        Assert.Equal("secret-a", sut.GetSecret("tenanta", "azuremonitor"));
        Assert.Equal("secret-b", sut.GetSecret("tenantb", "azuremonitor"));
    }
}
