using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Infrastructure.Persistence;
using OpsCopilot.SafeActions.Infrastructure.Policies;

namespace OpsCopilot.SafeActions.Infrastructure.Extensions;

public static class SafeActionsInfrastructureExtensions
{
    public static IServiceCollection AddSafeActionsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQL connection ──────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("Sql")
                            ?? configuration["SQL_CONNECTION_STRING"]
                            ?? throw new InvalidOperationException(
                                "SQL connection string not configured. " +
                                "Set ConnectionStrings:Sql or SQL_CONNECTION_STRING.");

        services.AddDbContext<SafeActionsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "safeActions");
            }));

        // ── Repository ──────────────────────────────────────────────
        services.AddScoped<IActionRecordRepository, SqlActionRecordRepository>();

        // ── Action executor (dry-run — deterministic, zero side-effects) ─
        services.AddSingleton<IActionExecutor, DryRunActionExecutor>();

        // ── Policy (default = allow-all; swap per-tenant later) ─────
        services.AddSingleton<ISafeActionPolicy, DefaultSafeActionPolicy>();

        return services;
    }
}
