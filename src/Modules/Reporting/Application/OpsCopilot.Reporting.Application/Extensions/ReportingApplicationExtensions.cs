using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Application.Services;

namespace OpsCopilot.Reporting.Application.Extensions;

public static class ReportingApplicationExtensions
{
    public static IServiceCollection AddReportingApplication(this IServiceCollection services)
    {
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        return services;
    }
}
