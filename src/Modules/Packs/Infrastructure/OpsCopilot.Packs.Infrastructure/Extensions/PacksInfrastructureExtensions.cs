using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure.Extensions;

public static class PacksInfrastructureExtensions
{
    /// <summary>
    /// Registers the filesystem-based pack loader, in-memory pack catalog,
    /// pack file reader, pack triage enricher, pack evidence executor,
    /// safe-action proposer, and safe-action recorder.
    /// </summary>
    public static IServiceCollection AddPacksInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IPackLoader, FileSystemPackLoader>();
        services.AddSingleton<IPackCatalog, PackCatalog>();
        services.AddSingleton<IPackFileReader, PackFileReader>();
        services.AddSingleton<IPackTriageEnricher, PackTriageEnricher>();
        services.AddSingleton<ITenantWorkspaceResolver, TenantWorkspaceResolver>();
        services.AddSingleton<IPackEvidenceExecutor, PackEvidenceExecutor>();
        services.AddSingleton<IPackSafeActionProposer, PackSafeActionProposer>();
        services.AddSingleton<IPackSafeActionRecorder, PackSafeActionRecorder>();

        return services;
    }
}
