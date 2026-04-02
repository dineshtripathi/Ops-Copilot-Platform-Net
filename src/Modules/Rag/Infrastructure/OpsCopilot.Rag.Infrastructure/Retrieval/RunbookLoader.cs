using Microsoft.Extensions.Logging;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Infrastructure.Retrieval;

/// <summary>
/// Loads markdown runbook files from a directory, parsing front-matter for tags.
/// Expected markdown format:
///   # Title
///   tags: tag1, tag2
///   (blank line)
///   Content...
/// </summary>
internal static class RunbookLoader
{
    public static IReadOnlyList<InMemoryRunbookRetrievalService.RunbookEntry> LoadFromDirectory(
        string directoryPath,
        ILogger logger)
    {
        if (!Directory.Exists(directoryPath))
        {
            logger.LogWarning("Runbook directory not found: {Path}. No runbooks loaded.", directoryPath);
            return [];
        }

        var files = Directory.GetFiles(directoryPath, "*.md", SearchOption.TopDirectoryOnly);
        logger.LogInformation("Loading {Count} runbook files from {Path}", files.Length, directoryPath);

        var entries = new List<InMemoryRunbookRetrievalService.RunbookEntry>(files.Length);

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                var entry = Parse(file, text);
                entries.Add(entry);
                logger.LogDebug("Loaded runbook '{Title}' from {File}", entry.Title, file);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load runbook from {File}", file);
            }
        }

        return entries;
    }

    internal static InMemoryRunbookRetrievalService.RunbookEntry Parse(string filePath, string text)
    {
        var id = Path.GetFileNameWithoutExtension(filePath);
        var lines = text.Split('\n');

        string title = id;
        var tags = new List<string>();
        int contentStart = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith("# ") && title == id)
            {
                title = trimmed[2..].Trim();
                contentStart = i + 1;
                continue;
            }

            if (trimmed.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                var tagsPart = trimmed["tags:".Length..];
                tags.AddRange(tagsPart
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                contentStart = i + 1;
                continue;
            }

            // Stop parsing front-matter on first non-empty, non-header, non-tags line
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                contentStart = i;
                break;
            }

            contentStart = i + 1;
        }

        var content = string.Join('\n', lines[contentStart..]).Trim();
        return new InMemoryRunbookRetrievalService.RunbookEntry(id, title, content, tags);
    }

    /// <summary>
    /// Loads all markdown runbooks from <paramref name="directoryPath"/> and converts
    /// them to <see cref="VectorRunbookDocument"/> instances ready for ingestion via
    /// <see cref="IRunbookIndexer"/>. Each document gets a deterministic <see cref="Guid"/>
    /// derived from the runbook ID so re-indexing is idempotent.
    /// </summary>
    internal static IReadOnlyList<VectorRunbookDocument> ToVectorDocuments(
        string   directoryPath,
        string   tenantId,
        ILogger  logger,
        string   embeddingModelId = "",
        string   embeddingVersion = "")
    {
        var entries = LoadFromDirectory(directoryPath, logger);
        var docs    = new List<VectorRunbookDocument>(entries.Count);

        foreach (var entry in entries)
        {
            docs.Add(new VectorRunbookDocument
            {
                Id               = GuidFromRunbookId(tenantId, entry.Id),
                TenantId         = tenantId,
                RunbookId        = entry.Id,
                Title            = entry.Title,
                Content          = entry.Content,
                Tags             = string.Join(", ", entry.Tags),
                EmbeddingModelId = embeddingModelId,
                EmbeddingVersion = embeddingVersion,
            });
        }

        return docs;
    }

    // Deterministic Guid: namespace-based UUID v5 (tenant + runbookId) so re-indexing
    // the same file produces the same vector-store key (idempotent upsert).
    private static readonly Guid _ns = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    private static Guid GuidFromRunbookId(string tenantId, string runbookId)
    {
        var input = $"{tenantId}:{runbookId}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);

        // Simple deterministic Guid: hash input into 16 bytes.
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant RFC 4122
        return new Guid(guidBytes);
    }
}
