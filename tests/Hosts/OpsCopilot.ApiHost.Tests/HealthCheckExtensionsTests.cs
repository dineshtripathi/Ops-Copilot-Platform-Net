using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpsCopilot.ApiHost.Infrastructure;
using Xunit;

namespace OpsCopilot.ApiHost.Tests;

/// <summary>
/// Slice 140 — Unit tests for health check DI registration.
/// These tests verify the service container shape without hitting real infrastructure.
/// </summary>
public sealed class HealthCheckExtensionsTests
{
    [Fact]
    public void AddOpsCopilotHealthChecks_RegistersHealthCheckService()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act
        services.AddLogging(); // DefaultHealthCheckService requires ILogger
        services.AddOpsCopilotHealthChecks(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var hcService = provider.GetService<HealthCheckService>();
        Assert.NotNull(hcService);
    }

    [Fact]
    public void AddOpsCopilotHealthChecks_NoSqlConfig_DoesNotRegisterSqlCheck()
    {
        // Arrange — no SQL config at all
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act
        services.AddOpsCopilotHealthChecks(config);

        // Assert — SqlHealthCheckOptions should NOT be in DI
        var provider = services.BuildServiceProvider();
        var sqlOpts = provider.GetService<SqlHealthCheckOptions>();
        Assert.Null(sqlOpts);
    }

    [Fact]
    public void AddOpsCopilotHealthChecks_WithSqlConfig_RegistersSqlCheckOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sql"] = "Server=localhost;Database=Test;Trusted_Connection=true;"
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddOpsCopilotHealthChecks(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var sqlOpts = provider.GetService<SqlHealthCheckOptions>();
        Assert.NotNull(sqlOpts);
        Assert.Equal("Server=localhost;Database=Test;Trusted_Connection=true;", sqlOpts.ConnectionString);
    }

    [Fact]
    public void AddOpsCopilotHealthChecks_NoRedisConfig_DoesNotRegisterRedisCheck()
    {
        // Arrange — no Redis config
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act
        services.AddOpsCopilotHealthChecks(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var redisOpts = provider.GetService<RedisHealthCheckOptions>();
        Assert.Null(redisOpts);
    }

    [Fact]
    public void AddOpsCopilotHealthChecks_WithRedisConfig_RegistersRedisCheckOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRuns:SessionStore:Provider"]          = "Redis",
                ["AgentRuns:SessionStore:ConnectionString"]  = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddOpsCopilotHealthChecks(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var redisOpts = provider.GetService<RedisHealthCheckOptions>();
        Assert.NotNull(redisOpts);
        Assert.Equal("localhost:6379", redisOpts.ConnectionString);
    }

    [Fact]
    public void AddOpsCopilotHealthChecks_RedisProviderNotSet_DoesNotRegisterRedis()
    {
        // Arrange — Redis connection string present but Provider not set to "Redis"
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRuns:SessionStore:Provider"]         = "InMemory",
                ["AgentRuns:SessionStore:ConnectionString"] = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddOpsCopilotHealthChecks(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var redisOpts = provider.GetService<RedisHealthCheckOptions>();
        Assert.Null(redisOpts);
    }
}
