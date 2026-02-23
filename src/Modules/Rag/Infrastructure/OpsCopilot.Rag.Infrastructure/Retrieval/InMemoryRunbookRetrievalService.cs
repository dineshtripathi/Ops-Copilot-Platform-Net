using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Application;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// Keyword-based in-memory retrieval service that loads markdown runbook files at startup.
/// Suitable for local development and early integration testing.
/// </summary>
internal sealed class InMemoryRunbookRetrievalService : IRunbookRetrievalService
{
    private readonly IReadOnlyList<RunbookEntry> _entries;
    private readonly ILogger<InMemoryRunbookRetrievalService> _logger;

    public InMemoryRunbookRetrievalService(
        IReadOnlyList<RunbookEntry> entries,
        ILogger<InMemoryRunbookRetrievalService> logger)
    {
        _entries = entries;
        _logger = logger;
    }

    public Task<IReadOnlyList<RunbookSearchResult>> SearchAsync(
        RunbookSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Searching {Count} runbooks for '{Query}'", _entries.Count, query.Query);

        var keywords = query.Query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .ToArray();

        var scored = _entries
            .Select(e => new { Entry = e, Score = ComputeScore(e, keywords) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(query.MaxResults)
            .Select(x => new RunbookSearchResult(
                x.Entry.Id,
                x.Entry.Title,
                ExtractSnippet(x.Entry.Content, keywords),
                x.Score))
            .ToList();

        _logger.LogDebug("Found {Hits} runbook hits for '{Query}'", scored.Count, query.Query);

        return Task.FromResult<IReadOnlyList<RunbookSearchResult>>(scored);
    }

    private static double ComputeScore(RunbookEntry entry, string[] keywords)
    {
        if (keywords.Length == 0) return 0;

        var lowerTitle = entry.Title.ToLowerInvariant();
        var lowerContent = entry.Content.ToLowerInvariant();
        var lowerTags = entry.Tags.Select(t => t.ToLowerInvariant()).ToArray();

        double score = 0;
        foreach (var keyword in keywords)
        {
            // Title match is weighted more heavily
            if (lowerTitle.Contains(keyword, StringComparison.Ordinal))
                score += 3.0;

            // Tag exact match
            if (lowerTags.Any(t => t.Equals(keyword, StringComparison.Ordinal)))
                score += 2.0;

            // Content occurrence count (capped)
            var occurrences = CountOccurrences(lowerContent, keyword);
            score += Math.Min(occurrences, 5) * 0.5;
        }

        // Normalize by keyword count
        return score / keywords.Length;
    }

    private static int CountOccurrences(string text, string keyword)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(keyword, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += keyword.Length;
        }
        return count;
    }

    private static string ExtractSnippet(string content, string[] keywords, int snippetLength = 200)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var lowerContent = content.ToLowerInvariant();

        // Find the first keyword occurrence to anchor the snippet
        int bestIdx = -1;
        foreach (var keyword in keywords)
        {
            int idx = lowerContent.IndexOf(keyword, StringComparison.Ordinal);
            if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                bestIdx = idx;
        }

        if (bestIdx < 0)
            return content.Length <= snippetLength
                ? content
                : content[..snippetLength] + "...";

        int start = Math.Max(0, bestIdx - snippetLength / 4);
        int end = Math.Min(content.Length, start + snippetLength);

        var snippet = content[start..end].Trim();
        if (start > 0) snippet = "..." + snippet;
        if (end < content.Length) snippet += "...";

        return snippet;
    }

    /// <summary>Pre-parsed runbook entry held in memory.</summary>
    internal sealed record RunbookEntry(string Id, string Title, string Content, IReadOnlyList<string> Tags);
}
