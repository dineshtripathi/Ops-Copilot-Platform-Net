using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Read-only catalog of all discovered packs, cached after first load.
/// </summary>
public interface IPackCatalog
{
    /// <summary>
    /// Returns the full list of loaded packs (valid and invalid).
    /// Results are cached after the first invocation.
    /// </summary>
    Task<IReadOnlyList<LoadedPack>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a single pack by name (case-insensitive), or null if not found.</summary>
    Task<LoadedPack?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all packs that target the given resource type (case-insensitive).</summary>
    Task<IReadOnlyList<LoadedPack>> FindByResourceTypeAsync(string resourceType, CancellationToken cancellationToken = default);

    /// <summary>Returns all packs that require the given minimum mode (case-insensitive).</summary>
    Task<IReadOnlyList<LoadedPack>> FindByMinimumModeAsync(string minimumMode, CancellationToken cancellationToken = default);

    /// <summary>Returns a detailed projection for a single pack, or null if not found.</summary>
    Task<PackDetails?> GetDetailsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns the runbook summaries for a pack, or null if the pack is not found.</summary>
    Task<IReadOnlyList<PackRunbookSummary>?> GetRunbooksAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns the evidence collector summaries for a pack, or null if the pack is not found.</summary>
    Task<IReadOnlyList<PackEvidenceCollectorSummary>?> GetEvidenceCollectorsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns the safe action summaries for a pack, or null if the pack is not found.</summary>
    Task<IReadOnlyList<PackSafeActionSummary>?> GetSafeActionsAsync(string name, CancellationToken cancellationToken = default);
}
