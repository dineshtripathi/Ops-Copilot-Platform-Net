using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Extensions;
using OpsCopilot.Prompting.Domain.Repositories;
using OpsCopilot.Prompting.Infrastructure.Adapters;
using OpsCopilot.Prompting.Infrastructure.Persistence;
using OpsCopilot.Prompting.Infrastructure.Repositories;

namespace OpsCopilot.Prompting.Infrastructure.Extensions;

public static class PromptingInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server), <see cref="IPromptTemplateRepository"/>,
    /// and <see cref="IPromptVersionService"/> (implemented by <see cref="SqlPromptVersionService"/>).
    ///
    /// Connection string resolution (first non-empty wins):
    ///   ConnectionStrings:Sql   — standard dotnet config / User Secrets
    ///   SQL_CONNECTION_STRING   — legacy flat env var
    /// </summary>
    public static IServiceCollection AddPromptingModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration["ConnectionStrings:Sql"]
                   ?? configuration["SQL_CONNECTION_STRING"]
                   ?? throw new InvalidOperationException(
                         "SQL connection string is not configured. " +
                         "Set 'ConnectionStrings:Sql' via User Secrets or Key Vault.");

        services.AddDbContext<PromptingDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "prompting");
            }));

        services.AddPromptingApplication();
        services.AddScoped<IPromptTemplateRepository, SqlPromptTemplateRepository>();
        services.AddScoped<IPromptVersionService,     SqlPromptVersionService>();

        // Replace the in-memory canary store with SQL-backed persistence.
        // Options are built here (not resolved from DI) to avoid Singleton→Scoped
        // captive dependency on DbContextOptions<PromptingDbContext>.
        var canaryOpts = new DbContextOptionsBuilder<PromptingDbContext>()
            .UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "prompting");
            })
            .Options;

        services.Replace(ServiceDescriptor.Singleton<ICanaryStore>(
            _ => new SqlCanaryStore(canaryOpts)));

        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations for the Prompting module.
    /// Call during startup after building the <see cref="WebApplication"/>.
    /// </summary>
    public static async Task UsePromptingMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromptingDbContext>();
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("PromptingDbContext migrations applied.");
    }
}

