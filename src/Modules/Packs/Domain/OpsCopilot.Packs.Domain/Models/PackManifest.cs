namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Strongly-typed representation of a pack.json manifest file.
/// All properties map 1-to-1 to the pack.json schema defined in the spec.
/// </summary>
public sealed record PackManifest(
    string Name,
    string Version,
    string Description,
    IReadOnlyList<string> ResourceTypes,
    string MinimumMode,
    IReadOnlyList<EvidenceCollector> EvidenceCollectors,
    IReadOnlyList<PackRunbook> Runbooks,
    IReadOnlyList<PackSafeAction> SafeActions);
