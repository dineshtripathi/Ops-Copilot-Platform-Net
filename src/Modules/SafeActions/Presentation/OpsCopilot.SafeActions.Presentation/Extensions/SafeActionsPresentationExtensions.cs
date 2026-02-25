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
        services.AddSingleton<IExecutionThrottlePolicy, InMemoryExecutionThrottlePolicy>();
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
