using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Reporting.Application.Extensions;
using OpsCopilot.Reporting.Infrastructure.Extensions;

namespace OpsCopilot.Reporting.Presentation.Extensions;

public static class ReportingPresentationExtensions
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReportingApplication();
        services.AddReportingInfrastructure(configuration);
        return services;
    }
}
