using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application;
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

        services.AddSingleton<IRunbookRetrievalService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<InMemoryRunbookRetrievalService>>();
            var loaderLogger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RunbookLoader).FullName!);

            var entries = RunbookLoader.LoadFromDirectory(runbookPath, loaderLogger);
            return new InMemoryRunbookRetrievalService(entries, logger);
        });

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
