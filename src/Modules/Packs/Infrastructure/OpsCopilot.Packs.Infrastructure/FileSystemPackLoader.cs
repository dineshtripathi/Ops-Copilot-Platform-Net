using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Scans a filesystem directory for pack folders, deserialises each pack.json,
/// and validates against the 15 rules defined in the Slice 34 specification.
/// </summary>
internal sealed partial class FileSystemPackLoader : IPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex KebabCasePattern();

    private static readonly string[] SecretMarkers =
    [
        "connectionstring", "clientsecret", "password",
        "apikey", "sas", "sig=", "bearer "
    ];

    private static readonly HashSet<string> ValidModes = new(StringComparer.Ordinal) { "A", "B", "C" };

    private readonly string _packsRootPath;
    private readonly ILogger<FileSystemPackLoader> _logger;

    public FileSystemPackLoader(IConfiguration configuration, ILogger<FileSystemPackLoader> logger)
    {
        _packsRootPath = configuration.GetValue<string>("Packs:RootPath") ?? "packs";
        _logger = logger;
    }

    public Task<IReadOnlyList<LoadedPack>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<LoadedPack>();

        if (!Directory.Exists(_packsRootPath))
        {
            _logger.LogWarning("Packs root directory '{PacksRoot}' does not exist. No packs loaded.", _packsRootPath);
            return Task.FromResult<IReadOnlyList<LoadedPack>>(results);
        }

        foreach (var packDir in Directory.GetDirectories(_packsRootPath).Order())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(packDir);
            var manifestPath = Path.Combine(packDir, "pack.json");

            // Rule 1: pack.json must exist
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning("Pack directory '{PackDir}' has no pack.json — skipped as invalid.", dirName);
                results.Add(new LoadedPack(
                    CreateEmptyManifest(dirName),
                    packDir,
                    new PackValidationResult(false, ["pack.json must exist."])));
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PackManifest>(json, JsonOptions);

                if (manifest is null)
                {
                    results.Add(new LoadedPack(
                        CreateEmptyManifest(dirName),
                        packDir,
                        new PackValidationResult(false, ["pack.json deserialized to null."])));
                    continue;
                }

                var validation = Validate(manifest, dirName, packDir);
                results.Add(new LoadedPack(manifest, packDir, validation));

                if (validation.IsValid)
                {
                    _logger.LogInformation("Pack '{PackName}' v{Version} loaded successfully.", manifest.Name, manifest.Version);
                }
                else
                {
                    _logger.LogWarning("Pack '{PackName}' has validation errors: {Errors}", manifest.Name, string.Join("; ", validation.Errors));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize pack.json in '{PackDir}'.", dirName);
                results.Add(new LoadedPack(
                    CreateEmptyManifest(dirName),
                    packDir,
                    new PackValidationResult(false, [$"Invalid JSON: {ex.Message}"])));
            }
        }

        // Deterministic ordering by name
        results.Sort((a, b) => string.Compare(a.Manifest.Name, b.Manifest.Name, StringComparison.Ordinal));

        _logger.LogInformation("Pack scan complete: {Total} total, {Valid} valid, {Invalid} invalid.",
            results.Count,
            results.Count(p => p.Validation.IsValid),
            results.Count(p => !p.Validation.IsValid));

        return Task.FromResult<IReadOnlyList<LoadedPack>>(results);
    }

    internal static PackValidationResult Validate(PackManifest manifest, string directoryName, string packDirectory)
    {
        var errors = new List<string>();

        // Rule 2: folder name must be kebab-case
        if (!KebabCasePattern().IsMatch(directoryName))
            errors.Add($"Folder name '{directoryName}' must be kebab-case.");

        // Rule 3: name must match folder
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            errors.Add("'name' is required.");
        }
        else if (!string.Equals(manifest.Name, directoryName, StringComparison.Ordinal))
        {
            errors.Add($"'name' ('{manifest.Name}') must match the directory name ('{directoryName}').");
        }

        // Rule 4: version non-empty
        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("'version' is required.");

        // Rule 5: description non-empty
        if (string.IsNullOrWhiteSpace(manifest.Description))
            errors.Add("'description' is required.");

        // Rule 6: resourceTypes non-empty array
        if (manifest.ResourceTypes is null || manifest.ResourceTypes.Count == 0)
            errors.Add("'resourceTypes' must be a non-empty array.");

        // Rule 7: minimumMode must be "A", "B", or "C"
        if (!ValidModes.Contains(manifest.MinimumMode ?? string.Empty))
            errors.Add($"'minimumMode' must be 'A', 'B', or 'C' (got '{manifest.MinimumMode}').");

        // Rules for evidenceCollectors
        if (manifest.EvidenceCollectors is { Count: > 0 })
        {
            var ecIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ec in manifest.EvidenceCollectors)
            {
                // Rule 10: IDs kebab-case
                if (!string.IsNullOrEmpty(ec.Id) && !KebabCasePattern().IsMatch(ec.Id))
                    errors.Add($"evidenceCollectors id '{ec.Id}' must be kebab-case.");

                // Rule 11: IDs unique within lists
                if (!string.IsNullOrEmpty(ec.Id) && !ecIds.Add(ec.Id))
                    errors.Add($"Duplicate evidenceCollectors id '{ec.Id}'.");

                // Rule 8: evidenceCollectors[].requiredMode must be "A", "B", or "C"
                if (!ValidModes.Contains(ec.RequiredMode ?? string.Empty))
                    errors.Add($"evidenceCollectors['{ec.Id}'].requiredMode must be 'A', 'B', or 'C' (got '{ec.RequiredMode}').");

                // Rule 12: queryFile must exist on disk (if specified)
                if (!string.IsNullOrEmpty(ec.QueryFile))
                {
                    var fullPath = Path.Combine(packDirectory, ec.QueryFile);
                    if (!File.Exists(fullPath))
                        errors.Add($"evidenceCollectors['{ec.Id}'].queryFile '{ec.QueryFile}' not found.");
                }
            }
        }

        // Rules for runbooks
        if (manifest.Runbooks is { Count: > 0 })
        {
            var rbIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rb in manifest.Runbooks)
            {
                // Rule 10: IDs kebab-case
                if (!string.IsNullOrEmpty(rb.Id) && !KebabCasePattern().IsMatch(rb.Id))
                    errors.Add($"runbooks id '{rb.Id}' must be kebab-case.");

                // Rule 11: IDs unique
                if (!string.IsNullOrEmpty(rb.Id) && !rbIds.Add(rb.Id))
                    errors.Add($"Duplicate runbooks id '{rb.Id}'.");

                // Rule 13: runbooks[].file must exist on disk
                if (!string.IsNullOrEmpty(rb.File))
                {
                    var fullPath = Path.Combine(packDirectory, rb.File);
                    if (!File.Exists(fullPath))
                        errors.Add($"runbooks['{rb.Id}'].file '{rb.File}' not found.");
                }
                else
                {
                    errors.Add($"runbooks['{rb.Id}'].file is required.");
                }
            }
        }

        // Rules for safeActions
        if (manifest.SafeActions is { Count: > 0 })
        {
            var saIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sa in manifest.SafeActions)
            {
                // Rule 10: IDs kebab-case
                if (!string.IsNullOrEmpty(sa.Id) && !KebabCasePattern().IsMatch(sa.Id))
                    errors.Add($"safeActions id '{sa.Id}' must be kebab-case.");

                // Rule 11: IDs unique
                if (!string.IsNullOrEmpty(sa.Id) && !saIds.Add(sa.Id))
                    errors.Add($"Duplicate safeActions id '{sa.Id}'.");

                // Rule 9: safeActions[].requiresMode must be "C"
                if (!string.Equals(sa.RequiresMode, "C", StringComparison.Ordinal))
                    errors.Add($"safeActions['{sa.Id}'].requiresMode must be 'C' (got '{sa.RequiresMode}').");

                // Rule 14: definitionFile must exist on disk (if specified)
                if (!string.IsNullOrEmpty(sa.DefinitionFile))
                {
                    var fullPath = Path.Combine(packDirectory, sa.DefinitionFile);
                    if (!File.Exists(fullPath))
                        errors.Add($"safeActions['{sa.Id}'].definitionFile '{sa.DefinitionFile}' not found.");
                }
            }
        }

        // Rule 15: no-secrets heuristic scan
        ScanForSecrets(packDirectory, errors);

        return new PackValidationResult(errors.Count == 0, errors);
    }

    private static void ScanForSecrets(string packDirectory, List<string> errors)
    {
        if (!Directory.Exists(packDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(packDirectory, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            // Only scan text-like files
            if (ext is not (".json" or ".kql" or ".md" or ".yaml" or ".yml" or ".txt"))
                continue;

            try
            {
                var content = File.ReadAllText(file);
                var contentLower = content.ToLowerInvariant();
                foreach (var marker in SecretMarkers)
                {
                    if (contentLower.Contains(marker, StringComparison.Ordinal))
                    {
                        var relativePath = Path.GetRelativePath(packDirectory, file);
                        errors.Add($"Possible secret marker '{marker}' found in '{relativePath}'.");
                        break; // One error per file is sufficient
                    }
                }
            }
            catch (IOException)
            {
                // Skip files that can't be read
            }
        }
    }

    private static PackManifest CreateEmptyManifest(string directoryName) =>
        new(directoryName, "0.0.0", string.Empty,
            Array.Empty<string>(), "A",
            Array.Empty<EvidenceCollector>(),
            Array.Empty<PackRunbook>(),
            Array.Empty<PackSafeAction>());
}
