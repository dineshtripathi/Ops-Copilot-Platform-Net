using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.Configuration;
using OpsCopilot.Tenancy.Infrastructure.Persistence;
using OpsCopilot.Tenancy.Infrastructure.Repositories;
using OpsCopilot.Tenancy.Infrastructure.Services;

namespace OpsCopilot.Tenancy.Infrastructure.Extensions;

public static class TenancyInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server), tenant repositories, and the config resolver.
    /// </summary>
    public static IServiceCollection AddTenancyInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration["ConnectionStrings:Sql"]
                   ?? configuration["SQL_CONNECTION_STRING"]
                   ?? throw new InvalidOperationException(
                       "SQL connection string is not configured. " +
                       "Set 'ConnectionStrings:Sql' via User Secrets or Key Vault, " +
                       "or set the 'SQL_CONNECTION_STRING' environment variable.");

        services.AddDbContext<TenancyDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "tenancy");
            }));

        services.Configure<GovernanceDefaultsConfig>(
            configuration.GetSection(GovernanceDefaultsConfig.SectionName));

        services.AddScoped<ITenantRegistry, SqlTenantRegistry>();
        services.AddScoped<ITenantConfigStore, SqlTenantConfigStore>();
        services.AddScoped<ITenantConfigResolver, TenantConfigResolver>();
        services.AddScoped<ITenantConfigProvider, TenantConfigProviderAdapter>();

        return services;
    }
}
