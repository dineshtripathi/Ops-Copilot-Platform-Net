using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application.Memory;

namespace OpsCopilot.Rag.Infrastructure.Memory;

/// <summary>
/// Queries two <see cref="IIncidentMemoryRetrievalService"/> instances and
/// returns a merged, deduplicated result set ordered by descending score.
/// Vision §6.8 — composite retrieval with dedupe; ACL enforcement is delegated
/// to each underlying service.
/// </summary>
internal sealed class CompositeIncidentMemoryRetrievalService : IIncidentMemoryRetrievalService
{
    private readonly IIncidentMemoryRetrievalService              _primary;
    private readonly IIncidentMemoryRetrievalService              _secondary;
    private readonly ILogger<CompositeIncidentMemoryRetrievalService> _logger;

    public CompositeIncidentMemoryRetrievalService(
        IIncidentMemoryRetrievalService                   primary,
        IIncidentMemoryRetrievalService                   secondary,
        ILogger<CompositeIncidentMemoryRetrievalService>  logger)
    {
        _primary   = primary;
        _secondary = secondary;
        _logger    = logger;
    }

    public async Task<IReadOnlyList<IncidentMemoryHit>> SearchAsync(
        IncidentMemoryQuery query,
        CancellationToken   cancellationToken = default)
    {
        var primaryTask   = SafeSearchAsync(_primary,   query, cancellationToken);
        var secondaryTask = SafeSearchAsync(_secondary, query, cancellationToken);

        await Task.WhenAll(primaryTask, secondaryTask);

        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<IncidentMemoryHit>(query.MaxResults * 2);

        // Primary results get priority during dedup.
        foreach (var hit in primaryTask.Result.Concat(secondaryTask.Result))
        {
            if (seen.Add(hit.RunId))
                merged.Add(hit);
        }

        return merged
            .OrderByDescending(h => h.Score)
            .Take(query.MaxResults)
            .ToList();
    }

    private async Task<IReadOnlyList<IncidentMemoryHit>> SafeSearchAsync(
        IIncidentMemoryRetrievalService service,
        IncidentMemoryQuery             query,
        CancellationToken               ct)
    {
        try
        {
            return await service.SearchAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Incident memory retrieval service '{Service}' failed. Returning empty results.",
                service.GetType().Name);
            return Array.Empty<IncidentMemoryHit>();
        }
    }
}
