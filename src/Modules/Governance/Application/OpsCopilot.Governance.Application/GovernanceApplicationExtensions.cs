using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;

namespace OpsCopilot.Governance.Application;

public static class GovernanceApplicationExtensions
{
    public static IServiceCollection AddGovernanceApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GovernanceOptions>(
            configuration.GetSection(GovernanceOptions.SectionName));

        services.AddSingleton<IToolAllowlistPolicy, DefaultToolAllowlistPolicy>();
        services.AddSingleton<ITokenBudgetPolicy, DefaultTokenBudgetPolicy>();
        services.AddSingleton<IDegradedModePolicy, DefaultDegradedModePolicy>();
        services.AddSingleton<ISessionPolicy, DefaultSessionPolicy>();

        return services;
    }
}
