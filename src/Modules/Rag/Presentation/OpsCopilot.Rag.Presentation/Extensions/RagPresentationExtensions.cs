using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Rag.Infrastructure.Extensions;

namespace OpsCopilot.Rag.Presentation.Extensions;

public static class RagPresentationExtensions
{
    /// <summary>
    /// Registers the complete RAG module (infrastructure + application services).
    /// Hosts reference only this facade — never the inner layers directly.
    /// </summary>
    public static IServiceCollection AddRagModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRagInfrastructure(configuration);
        return services;
    }
}
