using Microsoft.Extensions.Logging;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Reads individual pack files with path-traversal guard.
/// Rejects relative paths containing ".." segments or that resolve outside the pack directory.
/// </summary>
internal sealed class PackFileReader : IPackFileReader
{
    private readonly ILogger<PackFileReader> _logger;

    public PackFileReader(ILogger<PackFileReader> logger)
    {
        _logger = logger;
    }

    public async Task<string?> ReadFileAsync(string packPath, string relativeFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packPath) || string.IsNullOrWhiteSpace(relativeFilePath))
        {
            _logger.LogWarning("PackFileReader: empty packPath or relativeFilePath.");
            return null;
        }

        // Reject obvious traversal attempts
        if (relativeFilePath.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "PackFileReader: path traversal rejected for '{RelativePath}' in pack '{PackPath}'.",
                relativeFilePath, packPath);
            return null;
        }

        // Reject absolute paths
        if (Path.IsPathRooted(relativeFilePath))
        {
            _logger.LogWarning(
                "PackFileReader: absolute path rejected for '{RelativePath}' in pack '{PackPath}'.",
                relativeFilePath, packPath);
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(packPath, relativeFilePath));
        var normalizedPackPath = Path.GetFullPath(packPath);

        // Containment check: resolved path must start with the pack directory
        if (!fullPath.StartsWith(normalizedPackPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "PackFileReader: resolved path '{FullPath}' escapes pack directory '{PackPath}'.",
                fullPath, normalizedPackPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogDebug(
                "PackFileReader: file not found '{FullPath}' in pack '{PackPath}'.",
                fullPath, normalizedPackPath);
            return null;
        }

        return await File.ReadAllTextAsync(fullPath, ct);
    }
}
