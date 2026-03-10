using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Infrastructure.Persistence;

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
        services.AddSingleton<PackSafeActionRecorder>();
        services.AddSingleton<IProposalRecordingRetryPolicy, DefaultProposalRecordingRetryPolicy>();
        var cs = configuration.GetConnectionString("Sql")
            ?? configuration["SQL_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("Sql connection string is required for Packs infrastructure.");
        services.AddDbContextFactory<PacksDbContext>(options =>
            options.UseSqlServer(cs, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "packs");
            }));
        services.AddSingleton<SqlProposalDeadLetterRepository>();
        services.AddSingleton<IProposalDeadLetterStore>(sp => sp.GetRequiredService<SqlProposalDeadLetterRepository>());
        services.AddSingleton<IProposalDeadLetterRepository>(sp => sp.GetRequiredService<SqlProposalDeadLetterRepository>());
        services.AddSingleton<IPackSafeActionRecorder>(sp =>
            new DurablePackSafeActionRecorder(
                sp.GetRequiredService<PackSafeActionRecorder>(),
                sp.GetRequiredService<IProposalRecordingRetryPolicy>(),
                sp.GetRequiredService<IProposalDeadLetterStore>(),
                sp.GetRequiredService<ILogger<DurablePackSafeActionRecorder>>()));
        services.AddSingleton<ITargetScopeEvaluator, ConfigTargetScopeEvaluator>();

        return services;
    }
}
