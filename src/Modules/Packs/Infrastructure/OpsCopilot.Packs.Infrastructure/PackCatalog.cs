using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// In-memory cache over <see cref="IPackLoader"/>. Loads packs once on first access
/// and builds lookup indexes for efficient queries.
/// </summary>
internal sealed class PackCatalog : IPackCatalog
{
    private readonly IPackLoader _loader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<LoadedPack>? _cache;

    // ── Indexes (built once on first load) ────────────────────────
    private Dictionary<string, LoadedPack>? _nameIndex;
    private Dictionary<string, List<LoadedPack>>? _resourceTypeIndex;
    private Dictionary<string, List<LoadedPack>>? _modeIndex;

    public PackCatalog(IPackLoader loader)
    {
        _loader = loader;
    }

    // ── GetAllAsync (existing, unchanged) ─────────────────────────

    public async Task<IReadOnlyList<LoadedPack>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _cache!;
    }

    // ── Single-pack lookup ────────────────────────────────────────

    public async Task<LoadedPack?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _nameIndex!.TryGetValue(name, out var pack) ? pack : null;
    }

    // ── Search / filter ───────────────────────────────────────────

    public async Task<IReadOnlyList<LoadedPack>> FindByResourceTypeAsync(string resourceType, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _resourceTypeIndex!.TryGetValue(resourceType, out var list)
            ? list.AsReadOnly()
            : Array.Empty<LoadedPack>();
    }

    public async Task<IReadOnlyList<LoadedPack>> FindByMinimumModeAsync(string minimumMode, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _modeIndex!.TryGetValue(minimumMode, out var list)
            ? list.AsReadOnly()
            : Array.Empty<LoadedPack>();
    }

    // ── Projection helpers ────────────────────────────────────────

    public async Task<PackDetails?> GetDetailsAsync(string name, CancellationToken cancellationToken = default)
    {
        var pack = await GetByNameAsync(name, cancellationToken);
        return pack is null ? null : ToDetails(pack);
    }

    public async Task<IReadOnlyList<PackRunbookSummary>?> GetRunbooksAsync(string name, CancellationToken cancellationToken = default)
    {
        var pack = await GetByNameAsync(name, cancellationToken);
        return pack?.Manifest.Runbooks
            .Select(r => new PackRunbookSummary(r.Id, r.File))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<PackEvidenceCollectorSummary>?> GetEvidenceCollectorsAsync(string name, CancellationToken cancellationToken = default)
    {
        var pack = await GetByNameAsync(name, cancellationToken);
        return pack?.Manifest.EvidenceCollectors
            .Select(e => new PackEvidenceCollectorSummary(e.Id, e.RequiredMode, e.QueryFile))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<PackSafeActionSummary>?> GetSafeActionsAsync(string name, CancellationToken cancellationToken = default)
    {
        var pack = await GetByNameAsync(name, cancellationToken);
        return pack?.Manifest.SafeActions
            .Select(s => new PackSafeActionSummary(s.Id, s.RequiresMode, s.DefinitionFile))
            .ToList()
            .AsReadOnly();
    }

    // ── Internals ─────────────────────────────────────────────────

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
                return;

            _cache = await _loader.LoadAllAsync(cancellationToken);
            BuildIndexes(_cache);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void BuildIndexes(IReadOnlyList<LoadedPack> packs)
    {
        _nameIndex = new Dictionary<string, LoadedPack>(StringComparer.OrdinalIgnoreCase);
        _resourceTypeIndex = new Dictionary<string, List<LoadedPack>>(StringComparer.OrdinalIgnoreCase);
        _modeIndex = new Dictionary<string, List<LoadedPack>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packs)
        {
            // Name index — first-wins on collision (should not happen with valid packs)
            _nameIndex.TryAdd(pack.Manifest.Name, pack);

            // Resource type index — a pack can target multiple resource types
            foreach (var rt in pack.Manifest.ResourceTypes)
            {
                if (!_resourceTypeIndex.TryGetValue(rt, out var rtList))
                {
                    rtList = new List<LoadedPack>();
                    _resourceTypeIndex[rt] = rtList;
                }
                rtList.Add(pack);
            }

            // Minimum mode index
            var mode = pack.Manifest.MinimumMode;
            if (!_modeIndex.TryGetValue(mode, out var modeList))
            {
                modeList = new List<LoadedPack>();
                _modeIndex[mode] = modeList;
            }
            modeList.Add(pack);
        }
    }

    private static PackDetails ToDetails(LoadedPack pack) => new(
        Name: pack.Manifest.Name,
        Version: pack.Manifest.Version,
        Description: pack.Manifest.Description,
        ResourceTypes: pack.Manifest.ResourceTypes,
        MinimumMode: pack.Manifest.MinimumMode,
        EvidenceCollectorCount: pack.Manifest.EvidenceCollectors.Count,
        RunbookCount: pack.Manifest.Runbooks.Count,
        SafeActionCount: pack.Manifest.SafeActions.Count,
        IsValid: pack.Validation.IsValid,
        Errors: pack.Validation.Errors,
        PackPath: pack.PackPath);
}
