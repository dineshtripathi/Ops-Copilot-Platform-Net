using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.Governance.Application;
using OpsCopilot.Governance.Application.Configuration;

namespace OpsCopilot.Governance.Presentation.Extensions;

public static class GovernancePresentationExtensions
{
    /// <summary>
    /// Registers all Governance module services and optionally logs startup diagnostics.
    /// Hosts call only this method — inner layers are hidden behind this facade.
    /// </summary>
    public static IServiceCollection AddGovernanceModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger? startupLogger = null)
    {
        services.AddGovernanceApplication(configuration);

        if (startupLogger is not null)
            LogGovernanceDiagnostics(configuration, startupLogger);

        return services;
    }

    private static void LogGovernanceDiagnostics(
        IConfiguration configuration, ILogger logger)
    {
        var govSection = configuration.GetSection(GovernanceOptions.SectionName);
        var govOpts = new GovernanceOptions();
        govSection.Bind(govOpts);

        var tools = govOpts.Defaults.AllowedTools.Count > 0
            ? string.Join(", ", govOpts.Defaults.AllowedTools)
            : "(empty — allow all)";

        logger.LogInformation(
            "[Startup] Governance  AllowedTools=[{Tools}] | TriageEnabled={Triage} | TokenBudget={Budget} | TenantOverrides={Overrides}",
            tools,
            govOpts.Defaults.TriageEnabled,
            govOpts.Defaults.TokenBudget?.ToString() ?? "unlimited",
            govOpts.TenantOverrides.Count);

        logger.LogInformation(
            "[Startup] Governance  Tenant-aware resolution active — priority: SQL-backed → config-file TenantOverrides → Defaults | SessionTtlMinutes={Ttl}",
            govOpts.Defaults.SessionTtlMinutes);
    }
}
