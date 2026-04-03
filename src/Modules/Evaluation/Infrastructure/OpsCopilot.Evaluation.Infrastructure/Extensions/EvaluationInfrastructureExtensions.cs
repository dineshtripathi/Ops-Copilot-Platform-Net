using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpsCopilot.Evaluation.Application.OnlineEval;
using OpsCopilot.Evaluation.Infrastructure.Persistence;
using OpsCopilot.Evaluation.Infrastructure.Repositories;

namespace OpsCopilot.Evaluation.Infrastructure.Extensions;

public static class EvaluationInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server) and <see cref="IOnlineEvalRecorder"/>
    /// (implemented by <see cref="SqlOnlineEvalRecorder"/>).
    ///
    /// Connection string resolution (first non-empty wins):
    ///   ConnectionStrings:Sql   — standard dotnet config / User Secrets
    ///   SQL_CONNECTION_STRING   — legacy flat env var
    /// </summary>
    public static IServiceCollection AddEvaluationInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration["ConnectionStrings:Sql"]
                   ?? configuration["SQL_CONNECTION_STRING"]
                   ?? throw new InvalidOperationException(
                         "SQL connection string is not configured. " +
                         "Set 'ConnectionStrings:Sql' via User Secrets or Key Vault.");

        services.AddDbContext<EvaluationDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "eval");
            }));

        // Replace the null recorder with SQL-backed persistence.
        // Options are built here (not resolved from DI) to avoid Singleton→Scoped
        // captive dependency on DbContextOptions<EvaluationDbContext>.
        var evalOpts = new DbContextOptionsBuilder<EvaluationDbContext>()
            .UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "eval");
            })
            .Options;

        services.Replace(ServiceDescriptor.Singleton<IOnlineEvalRecorder>(
            _ => new SqlOnlineEvalRecorder(evalOpts)));

        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations for the Evaluation module.
    /// Call during startup after building the <see cref="WebApplication"/>.
    /// </summary>
    public static async Task UseEvaluationMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EvaluationDbContext>();
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("EvaluationDbContext migrations applied.");
    }
}
