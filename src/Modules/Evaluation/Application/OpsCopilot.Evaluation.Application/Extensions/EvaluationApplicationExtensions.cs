using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Application.Scenarios;
using OpsCopilot.Evaluation.Application.Services;

namespace OpsCopilot.Evaluation.Application.Extensions;

public static class EvaluationApplicationExtensions
{
    public static IServiceCollection AddEvaluationApplication(this IServiceCollection services)
    {
        // AlertIngestion scenarios
        services.AddSingleton<IEvaluationScenario, AlertIngestion_AzureMonitorParseScenario>();
        services.AddSingleton<IEvaluationScenario, AlertIngestion_DatadogProviderScenario>();
        services.AddSingleton<IEvaluationScenario, AlertIngestion_EmptyPayloadRejectionScenario>();
        services.AddSingleton<IEvaluationScenario, AlertIngestion_FingerprintDeterminismScenario>();

        // SafeActions scenarios
        services.AddSingleton<IEvaluationScenario, SafeActions_ActionTypeClassificationScenario>();
        services.AddSingleton<IEvaluationScenario, SafeActions_EmptyActionListScenario>();
        services.AddSingleton<IEvaluationScenario, SafeActions_DryRunGuardScenario>();
        services.AddSingleton<IEvaluationScenario, SafeActions_ReplayDetectionScenario>();

        // Reporting scenarios
        services.AddSingleton<IEvaluationScenario, Reporting_SummaryTotalsScenario>();
        services.AddSingleton<IEvaluationScenario, Reporting_RecentLimitClampScenario>();
        services.AddSingleton<IEvaluationScenario, Reporting_TenantFilterScenario>();

        // Services
        services.AddSingleton<EvaluationScenarioCatalog>();
        services.AddSingleton<EvaluationRunner>();

        return services;
    }
}
