namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// An evidence collector declared by a pack, with its required operating mode
/// and an optional query file path (relative to the pack directory).
/// </summary>
public sealed record EvidenceCollector(
    string Id,
    string RequiredMode,
    string? QueryFile);
