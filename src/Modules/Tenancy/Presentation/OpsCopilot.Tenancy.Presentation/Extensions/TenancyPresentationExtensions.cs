using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.Tenancy.Infrastructure.Extensions;
using OpsCopilot.Tenancy.Infrastructure.Persistence;
using OpsCopilot.Tenancy.Presentation.Endpoints;

namespace OpsCopilot.Tenancy.Presentation.Extensions;

public static class TenancyPresentationExtensions
{
    public static IServiceCollection AddTenancyModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenancyInfrastructure(configuration);
        return services;
    }

    public static async Task UseTenancyMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        await db.Database.MigrateAsync();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TenancyDbContext>>();
        logger.LogInformation("TenancyDbContext migrations applied.");
    }

    public static WebApplication MapTenancyEndpoints(this WebApplication app)
    {
        TenancyEndpoints.Map(app);
        return app;
    }
}
