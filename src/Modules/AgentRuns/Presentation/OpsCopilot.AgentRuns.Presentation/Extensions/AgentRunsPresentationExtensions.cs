using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Extensions;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;

namespace OpsCopilot.AgentRuns.Presentation.Extensions;

public static class AgentRunsPresentationExtensions
{
    /// <summary>
    /// Registers all AgentRuns module services: Application, Infrastructure, and Presentation layers.
    /// Hosts call only this method — inner layers are hidden behind this facade.
    /// </summary>
    public static IServiceCollection AddAgentRunsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAgentRunsApplication();
        services.AddAgentRunsInfrastructure(configuration);
        // Reserved for future presentation-layer registrations (e.g. request validators).
        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations for the AgentRuns module.
    /// Call during startup after building the WebApplication.
    /// </summary>
    public static async Task UseAgentRunsMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("AgentRunsDbContext migrations applied.");
    }
}
