using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Evaluation.Application.Extensions;

namespace OpsCopilot.Evaluation.Presentation.Extensions;

public static class EvaluationPresentationExtensions
{
    public static IServiceCollection AddEvaluationModule(this IServiceCollection services)
    {
        services.AddEvaluationApplication();
        return services;
    }
}
