using Microsoft.Extensions.Logging;

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
}
