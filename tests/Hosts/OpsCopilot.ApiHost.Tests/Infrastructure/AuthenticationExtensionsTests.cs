using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.ApiHost.Infrastructure;
using Xunit;

namespace OpsCopilot.ApiHost.Tests.Infrastructure;

/// <summary>
/// Slice 149 — Unit tests for authentication DI registration.
/// Verifies service container shape without hitting real Entra endpoints.
/// Full 401/200 HTTP-level tests belong in the integration test suite (Slice 156).
/// </summary>
public sealed class AuthenticationExtensionsTests
{
    // ── DevBypass ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddOpsCopilotAuthentication_DevBypass_RegistersAuthorizationService()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevBypass"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOpsCopilotAuthentication(config);

        // Assert
        using var sp = services.BuildServiceProvider();
        var authService = sp.GetService<IAuthorizationService>();
        Assert.NotNull(authService);
    }

    [Fact]
    public void AddOpsCopilotAuthentication_DevBypass_DoesNotThrow()
    {
        // Arrange — DevBypass=true; TenantId and Audience absent — should not throw
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevBypass"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert — no exception
        var ex = Record.Exception(() => services.AddOpsCopilotAuthentication(config));
        Assert.Null(ex);
    }

    // ── Production (Entra JWT Bearer) ─────────────────────────────────────────

    [Fact]
    public void AddOpsCopilotAuthentication_NoDevBypass_MissingTenantId_Throws()
    {
        // Arrange — DevBypass=false, TenantId absent
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevBypass"] = "false",
                ["Authentication:Entra:Audience"] = "api://opscopilot"
            })
            .Build();

        var services = new ServiceCollection();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddOpsCopilotAuthentication(config));
        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void AddOpsCopilotAuthentication_NoDevBypass_MissingAudience_Throws()
    {
        // Arrange — DevBypass=false, Audience absent
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevBypass"] = "false",
                ["Authentication:Entra:TenantId"] = "00000000-0000-0000-0000-000000000001"
            })
            .Build();

        var services = new ServiceCollection();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddOpsCopilotAuthentication(config));
        Assert.Contains("Audience", ex.Message);
    }

    [Fact]
    public void AddOpsCopilotAuthentication_NoDevBypass_ValidConfig_RegistersAuthorizationService()
    {
        // Arrange — both TenantId and Audience provided
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevBypass"] = "false",
                ["Authentication:Entra:TenantId"] = "00000000-0000-0000-0000-000000000001",
                ["Authentication:Entra:Audience"] = "api://opscopilot"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOpsCopilotAuthentication(config);

        // Assert — IAuthorizationService present
        using var sp = services.BuildServiceProvider();
        var authService = sp.GetService<IAuthorizationService>();
        Assert.NotNull(authService);
    }

    // ── Default (no DevBypass key) ────────────────────────────────────────────

    [Fact]
    public void AddOpsCopilotAuthentication_NoAuthKey_DefaultsToDevBypassFalse_ThrowsWithoutEntraConfig()
    {
        // Arrange — no Authentication section at all → DevBypass defaults to false
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act + Assert — throws because TenantId is missing
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddOpsCopilotAuthentication(config));
        Assert.Contains("TenantId", ex.Message);
    }
}
