using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// Queries two <see cref="IRunbookRetrievalService"/> instances and returns a
/// merged, deduplicated result set ordered by descending score. Vision §6.8 —
/// composite retrieval with dedupe and tenant ACL enforcement delegated to each
/// underlying service.
/// </summary>
internal sealed class CompositeRunbookRetrievalService : IRunbookRetrievalService
{
    private readonly IRunbookRetrievalService              _primary;
    private readonly IRunbookRetrievalService              _secondary;
    private readonly ILogger<CompositeRunbookRetrievalService> _logger;

    public CompositeRunbookRetrievalService(
        IRunbookRetrievalService                   primary,
        IRunbookRetrievalService                   secondary,
        ILogger<CompositeRunbookRetrievalService>  logger)
    {
        _primary   = primary;
        _secondary = secondary;
        _logger    = logger;
    }

    public async Task<IReadOnlyList<RunbookSearchResult>> SearchAsync(
        RunbookSearchQuery query,
        CancellationToken  cancellationToken = default)
    {
        var primaryTask   = SafeSearchAsync(_primary,   query, cancellationToken);
        var secondaryTask = SafeSearchAsync(_secondary, query, cancellationToken);

        await Task.WhenAll(primaryTask, secondaryTask);

        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged  = new List<RunbookSearchResult>(query.MaxResults * 2);

        // Interleave primary then secondary to give primary priority on ties.
        foreach (var r in primaryTask.Result.Concat(secondaryTask.Result))
        {
            if (seen.Add(r.RunbookId))
                merged.Add(r);
        }

        return merged
            .OrderByDescending(r => r.Score)
            .Take(query.MaxResults)
            .ToList();
    }

    private async Task<IReadOnlyList<RunbookSearchResult>> SafeSearchAsync(
        IRunbookRetrievalService service,
        RunbookSearchQuery       query,
        CancellationToken        ct)
    {
        try
        {
            return await service.SearchAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Runbook retrieval service '{Service}' failed. Returning empty results.",
                service.GetType().Name);
            return Array.Empty<RunbookSearchResult>();
        }
    }
}
