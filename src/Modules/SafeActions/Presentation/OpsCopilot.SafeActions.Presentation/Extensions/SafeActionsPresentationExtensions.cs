using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.SafeActions.Application.Extensions;
using OpsCopilot.SafeActions.Infrastructure.Extensions;
using OpsCopilot.SafeActions.Infrastructure.Persistence;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.SafeActions.Presentation.Identity;
using OpsCopilot.SafeActions.Presentation.Telemetry;
using OpsCopilot.SafeActions.Presentation.Throttling;
using StackExchange.Redis;

namespace OpsCopilot.SafeActions.Presentation.Extensions;

public static class SafeActionsPresentationExtensions
{
    /// <summary>
    /// Registers all SafeActions module services (Application + Infrastructure).
    /// </summary>
    public static IServiceCollection AddSafeActionsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSafeActionsApplication();
        services.AddSafeActionsInfrastructure(configuration);
        services.AddSingleton<IActorIdentityResolver, ClaimsActorIdentityResolver>();
        services.AddSingleton<ISafeActionsTelemetry, SafeActionsTelemetry>();

        // Throttle policy: use Redis if configured, otherwise fall back to in-process.
        var throttleProvider = configuration["SafeActions:ExecutionThrottle:Provider"];
        if (string.Equals(throttleProvider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            var redisConn = configuration["SafeActions:ExecutionThrottle:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "SafeActions:ExecutionThrottle:Provider is 'Redis' but no ConnectionString is configured. "
                    + "Set 'SafeActions:ExecutionThrottle:ConnectionString' via User Secrets, Key Vault, or environment variable.");

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
            services.AddSingleton<IExecutionThrottlePolicy, RedisExecutionThrottlePolicy>();
        }
        else
        {
            // Default: single-node in-process throttling (dev / single-replica).
            services.AddSingleton<IExecutionThrottlePolicy, InMemoryExecutionThrottlePolicy>();
        }

        return services;
    }

    /// <summary>
    /// Applies EF Core migrations for the SafeActions module.
    /// </summary>
    public static async Task UseSafeActionsMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SafeActionsDbContext>();
        await db.Database.MigrateAsync();
    }

}
