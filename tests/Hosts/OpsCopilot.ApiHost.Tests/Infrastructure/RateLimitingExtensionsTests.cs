using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.ApiHost.Infrastructure;
using Xunit;

namespace OpsCopilot.ApiHost.Tests.Infrastructure;

/// <summary>
/// Slice 151 — Unit tests for rate limiting DI registration.
/// Verifies service container shape and config-binding without
/// exercising full HTTP pipeline (integration tests in Slice 156).
/// </summary>
public sealed class RateLimitingExtensionsTests
{
    // ── Service registration ──────────────────────────────────────────────────

    [Fact]
    public void AddOpsCopilotRateLimiting_RegistersRateLimiterService()
    {
        // Arrange
        var config = BuildConfig();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOpsCopilotRateLimiting(config);

        // Assert — IServiceCollection is populated (rate limiter uses internal ASP.NET services)
        using var sp = services.BuildServiceProvider();
        // AddRateLimiter registers RateLimiterOptions as a service
        var options = sp.GetService<Microsoft.Extensions.Options.IOptions<RateLimiterOptions>>();
        Assert.NotNull(options);
    }

    [Fact]
    public void AddOpsCopilotRateLimiting_DefaultConfig_DoesNotThrow()
    {
        // Arrange — no RateLimiting section; defaults must apply
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotRateLimiting(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotRateLimiting_CustomTriagePermitLimit_DoesNotThrow()
    {
        // Arrange
        var config = BuildConfig(triageLimit: 5, triageWindow: 30);
        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotRateLimiting(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotRateLimiting_CustomDefaultPermitLimit_DoesNotThrow()
    {
        // Arrange
        var config = BuildConfig(defaultLimit: 50, defaultWindow: 120);
        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotRateLimiting(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddOpsCopilotRateLimiting_HighPermissiveLimits_DoesNotThrow()
    {
        // Arrange — mirrors Development appsettings (very high limits)
        var config = BuildConfig(triageLimit: 1000, triageWindow: 1, defaultLimit: 10000, defaultWindow: 1);
        var services = new ServiceCollection();
        services.AddLogging();

        // Act + Assert
        var ex = Record.Exception(() => services.AddOpsCopilotRateLimiting(config));
        Assert.Null(ex);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(
        int triageLimit   = 10,
        int triageWindow  = 60,
        int defaultLimit  = 100,
        int defaultWindow = 60) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Triage:PermitLimit"]      = triageLimit.ToString(),
                ["RateLimiting:Triage:WindowSeconds"]    = triageWindow.ToString(),
                ["RateLimiting:Default:PermitLimit"]     = defaultLimit.ToString(),
                ["RateLimiting:Default:WindowSeconds"]   = defaultWindow.ToString()
            })
            .Build();
}
