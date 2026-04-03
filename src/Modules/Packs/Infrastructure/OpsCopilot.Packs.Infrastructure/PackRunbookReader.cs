using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Searches every loaded pack for a runbook file at <c>runbooks/{runbookName}</c>
/// and returns the first match.
/// All name inputs are validated before any filesystem access to prevent
/// path-traversal attacks.
/// </summary>
internal sealed partial class PackRunbookReader : IPackRunbookReader
{
    // Matches safe filenames like "exception-diagnosis.md" — lowercase letters,
    // digits, and hyphens only, must end in ".md", no path separators.
    [GeneratedRegex(@"^[a-z][a-z0-9-]*\.md$", RegexOptions.None)]
    private static partial Regex SafeRunbookNameRegex();

    private readonly IPackCatalog _catalog;
    private readonly IPackFileReader _fileReader;
    private readonly ILogger<PackRunbookReader> _logger;

    public PackRunbookReader(
        IPackCatalog catalog,
        IPackFileReader fileReader,
        ILogger<PackRunbookReader> logger)
    {
        _catalog    = catalog;
        _fileReader = fileReader;
        _logger     = logger;
    }

    public async Task<string?> ReadAsync(string runbookName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runbookName))
        {
            _logger.LogWarning("PackRunbookReader: empty runbook name.");
            return null;
        }

        // Strict name validation — no path separators, no traversal
        if (!SafeRunbookNameRegex().IsMatch(runbookName))
        {
            _logger.LogWarning(
                "PackRunbookReader: runbook name '{Name}' rejected (failed safe-name validation).",
                runbookName);
            return null;
        }

        var relativePath = $"runbooks/{runbookName}";

        var packs = await _catalog.GetAllAsync(ct).ConfigureAwait(false);

        foreach (var pack in packs)
        {
            var content = await _fileReader.ReadFileAsync(pack.PackPath, relativePath, ct)
                                           .ConfigureAwait(false);
            if (content is not null)
            {
                _logger.LogDebug(
                    "PackRunbookReader: found '{Name}' in pack '{PackName}'.",
                    runbookName, pack.Manifest.Name);
                return content;
            }
        }

        _logger.LogWarning(
            "PackRunbookReader: runbook '{Name}' not found in any pack.",
            runbookName);
        return null;
    }
}
