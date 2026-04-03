using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.Prompting;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Services;
using OpsCopilot.Prompting.Domain.Repositories;

namespace OpsCopilot.Prompting.Application.Extensions;

public static class PromptingApplicationExtensions
{
    public static IServiceCollection AddPromptingApplication(this IServiceCollection services)
    {
        services.AddSingleton<ICanaryStore, InMemoryCanaryStore>();
        services.AddSingleton<PromotionGateService>();
        services.AddSingleton<IFeedbackQualityGate, PromotionGateBridge>();

        // Decorator: CanaryPromptStrategy wraps PromptRegistryService
        services.AddScoped<IPromptRegistry>(sp =>
        {
            var repo = sp.GetRequiredService<IPromptTemplateRepository>();
            var inner = new PromptRegistryService(repo);
            var store = sp.GetRequiredService<ICanaryStore>();
            return new CanaryPromptStrategy(inner, store);
        });

        return services;
    }
}
