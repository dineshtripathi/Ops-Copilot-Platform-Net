using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;

namespace OpsCopilot.Governance.Application;

public static class GovernanceApplicationExtensions
{
    public static IServiceCollection AddGovernanceApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GovernanceOptions>(
            configuration.GetSection(GovernanceOptions.SectionName));

        // Tenant-aware resolver: SQL → config-file overrides → defaults
        services.AddScoped<ITenantAwareGovernanceOptionsResolver, TenantAwareGovernanceOptionsResolver>();

        // Scoped policies — depend on the scoped resolver for tenant-aware config
        services.AddScoped<IToolAllowlistPolicy, DefaultToolAllowlistPolicy>();
        services.AddScoped<ITokenBudgetPolicy, DefaultTokenBudgetPolicy>();
        services.AddScoped<ISessionPolicy, DefaultSessionPolicy>();

        // DegradedModePolicy has no config dependency — stays singleton
        services.AddSingleton<IDegradedModePolicy, DefaultDegradedModePolicy>();

        return services;
    }
}
