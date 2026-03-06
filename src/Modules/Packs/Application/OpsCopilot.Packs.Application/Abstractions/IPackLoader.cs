using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Discovers and loads packs from a configured root directory,
/// deserialising each pack.json and validating against PACKS.md rules.
/// </summary>
public interface IPackLoader
{
    /// <summary>
    /// Scans the packs root directory and returns every discovered pack
    /// with its validation result (valid or invalid with error list).
    /// </summary>
    Task<IReadOnlyList<LoadedPack>> LoadAllAsync(CancellationToken cancellationToken = default);
}
