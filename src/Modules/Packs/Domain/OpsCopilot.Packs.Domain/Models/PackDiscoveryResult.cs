namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Aggregate result of a pack discovery scan, partitioning loaded packs
/// into valid and invalid collections with a generation timestamp.
/// </summary>
public sealed record PackDiscoveryResult(
    IReadOnlyList<LoadedPack> ValidPacks,
    IReadOnlyList<LoadedPack> InvalidPacks,
    DateTime GeneratedAtUtc);
