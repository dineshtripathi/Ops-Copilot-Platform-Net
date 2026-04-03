using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using OpsCopilot.Rag.Application;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Rag.Domain;
using OpsCopilot.Rag.Application.Acl;
using OpsCopilot.Rag.Infrastructure.Acl;
using OpsCopilot.Rag.Infrastructure.Memory;
using OpsCopilot.Rag.Infrastructure.Retrieval;

namespace OpsCopilot.Rag.Infrastructure.Extensions;

public static class RagInfrastructureExtensions
{
    /// <summary>
    /// Registers RAG module services. Currently uses in-memory keyword search
    /// backed by markdown files loaded from disk.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRagInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runbookPath = ResolveRunbookPath(configuration);

        // ── Runbook retrieval (opt-in vector search) ──────────────────────────
        // Set Rag:UseVectorRunbooks=true + register VectorStoreCollection<Guid, VectorRunbookDocument>
        // to enable semantic search over runbooks using Azure AI Search (or compatible store).
        // Default: in-memory keyword search loaded from markdown files.
        if (bool.TryParse(configuration["Rag:UseVectorRunbooks"], out var useVectorRunbooks) && useVectorRunbooks)
        {
            // Embedding version config (PDD §2.2.10 Hard Invariant)
            var embeddingModelId = configuration["Rag:EmbeddingModelId"] ?? "text-embedding-3-small";
            var embeddingVersion = configuration["Rag:EmbeddingVersion"] ?? "1";

            services.AddSingleton<IRunbookRetrievalService>(sp =>
                new VectorRunbookRetrievalService(
                    sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                    sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>(),
                    sp.GetRequiredService<ILogger<VectorRunbookRetrievalService>>(),
                    embeddingVersion));

            services.AddSingleton<IRunbookIndexer>(sp =>
                new VectorRunbookIndexer(
                    sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                    sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>(),
                    embeddingModelId,
                    embeddingVersion));
        }
        else
        {
            services.AddSingleton<IRunbookRetrievalService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<InMemoryRunbookRetrievalService>>();
                var loaderLogger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(RunbookLoader).FullName!);

                var entries = RunbookLoader.LoadFromDirectory(runbookPath, loaderLogger);
                return new InMemoryRunbookRetrievalService(entries, logger);
            });

            services.AddSingleton<IRunbookIndexer, NullRagRunbookIndexer>();
        }

        services.AddSingleton<IIncidentMemoryRetrievalService, InMemoryIncidentMemoryRetrievalService>();

        // ── Vector memory indexer (opt-in) ────────────────────────────────────
        // Requires IEmbeddingGenerator and VectorStoreCollection to be registered
        // externally (e.g. by ApiHost via Azure OpenAI + Azure AI Search).
        // Default: NullRagIncidentMemoryIndexer (no-op, safe for all environments).
        if (bool.TryParse(configuration["Rag:UseVectorMemory"], out var useVectorMemory) && useVectorMemory)
            services.AddSingleton<IIncidentMemoryIndexer, VectorIncidentMemoryIndexer>();
        else
            services.AddSingleton<IIncidentMemoryIndexer, NullRagIncidentMemoryIndexer>();

        // ── Runbook reindex service (Slice 183) ──────────────────────────────
        // Admin endpoint uses this to re-ingest runbooks from disk on-demand.
        services.AddSingleton<IRunbookReindexService>(sp =>
            new RunbookReindexService(
                sp.GetRequiredService<IRunbookIndexer>(),
                runbookPath,
                sp.GetRequiredService<ILogger<RunbookReindexService>>()));

        // ── ACL filter service (§6.17) ────────────────────────────────────────
        // Null/passthrough by default. When AzureAISearch is the vector backend the
        // tenant-scoped implementation overrides the null one (last-registration-wins).
        services.AddSingleton<IAclFilterService, NullAclFilterService>();

        if (string.Equals(configuration["Rag:VectorBackend"], "AzureAISearch",
                StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAclFilterService, TenantGroupRoleAclFilterService>();

        // Slice 191: Entra-group-aware ACL filter (§6.18).
        // Only active when AzureAISearch is the backend AND Rag:UseGroupAcl = true.
        // Last-registration-wins: overrides both Null and TenantGroupRole registrations above.
        if (bool.TryParse(configuration["Rag:UseGroupAcl"], out var useGroupAcl) && useGroupAcl
            && string.Equals(configuration["Rag:VectorBackend"], "AzureAISearch",
                StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAclFilterService, EntraGroupAclFilterService>();

        return services;
    }

    private static string ResolveRunbookPath(IConfiguration configuration)
    {
        // Config key → env var → default
        var path = configuration["Rag:RunbookPath"]
                   ?? Environment.GetEnvironmentVariable("RAG_RUNBOOK_PATH");

        if (!string.IsNullOrWhiteSpace(path))
            return path;

        // Default: docs/runbooks/dev-seed relative to solution root.
        // Walk up from AppContext.BaseDirectory to find the .sln file,
        // which works regardless of which host (ApiHost or McpHost) loaded
        // this assembly.
        var solutionRoot = DiscoverSolutionRoot()
                           ?? throw new InvalidOperationException(
                               "Cannot find solution root (*.sln) from " + AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "docs", "runbooks", "dev-seed");
    }

    /// <summary>
    /// Walks up the directory tree from <see cref="AppContext.BaseDirectory"/>
    /// until it finds a folder containing a *.sln file.
    /// </summary>
    private static string? DiscoverSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
