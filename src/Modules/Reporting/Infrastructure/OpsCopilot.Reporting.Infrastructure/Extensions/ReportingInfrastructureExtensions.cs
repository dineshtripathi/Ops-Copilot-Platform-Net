using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Queries;

namespace OpsCopilot.Reporting.Infrastructure.Extensions;

public static class ReportingInfrastructureExtensions
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Sql")
                            ?? configuration["SQL_CONNECTION_STRING"]
                            ?? throw new InvalidOperationException(
                                "SQL connection string not configured. " +
                                "Set ConnectionStrings:Sql or SQL_CONNECTION_STRING.");

        services.AddDbContext<ReportingReadDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IReportingQueryService, ReportingQueryService>();
        services.AddSingleton<IPlatformReportingQueryService, PlatformReportingQueryService>();

        return services;
    }
}
