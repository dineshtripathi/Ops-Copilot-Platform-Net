using Microsoft.Extensions.DependencyInjection;

namespace OpsCopilot.Reporting.Application.Extensions;

public static class ReportingApplicationExtensions
{
    public static IServiceCollection AddReportingApplication(this IServiceCollection services)
    {
        // No application-level services to register yet (interface-only layer).
        return services;
    }
}
