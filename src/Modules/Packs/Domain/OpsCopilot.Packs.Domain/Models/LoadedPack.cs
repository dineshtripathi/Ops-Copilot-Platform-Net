namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// A pack that has been loaded from disk and validated.
/// Combines the manifest, its filesystem path, and validation outcome.
/// </summary>
public sealed record LoadedPack(
    PackManifest Manifest,
    string PackPath,
    PackValidationResult Validation);
