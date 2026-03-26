using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Services;

namespace OpsCopilot.Prompting.Application.Extensions;

public static class PromptingApplicationExtensions
{
    public static IServiceCollection AddPromptingApplication(this IServiceCollection services)
    {
        services.AddScoped<IPromptRegistry, PromptRegistryService>();
        return services;
    }
}
