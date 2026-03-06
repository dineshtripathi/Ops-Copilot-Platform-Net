namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Read-only projection of a loaded pack with summary counts.
/// Used by catalog query endpoints.
/// </summary>
public sealed record PackDetails(
    string Name,
    string Version,
    string Description,
    IReadOnlyList<string> ResourceTypes,
    string MinimumMode,
    int EvidenceCollectorCount,
    int RunbookCount,
    int SafeActionCount,
    bool IsValid,
    IReadOnlyList<string> Errors,
    string PackPath);
