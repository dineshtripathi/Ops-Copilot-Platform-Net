using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Discovers Mode-A pack runbooks and evidence collectors, reads their content,
/// and returns a <see cref="PackTriageEnrichment"/> payload.
/// </summary>
internal sealed class PackTriageEnricher : IPackTriageEnricher
{
    private const string ModeA = "A";
    private const int MaxSnippetLength = 2_000;

    private readonly IPackCatalog _catalog;
    private readonly IPackFileReader _fileReader;
    private readonly ILogger<PackTriageEnricher> _logger;

    public PackTriageEnricher(
        IPackCatalog catalog,
        IPackFileReader fileReader,
        ILogger<PackTriageEnricher> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PackTriageEnrichment> EnrichAsync(CancellationToken ct = default)
    {
        var runbooks = new List<PackRunbookDetail>();
        var evidenceCollectors = new List<PackEvidenceCollectorDetail>();
        var errors = new List<string>();

        IReadOnlyList<Domain.Models.LoadedPack> packs;
        try
        {
            packs = await _catalog.FindByMinimumModeAsync(ModeA, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query pack catalog for Mode-A packs");
            errors.Add($"Pack catalog query failed: {ex.Message}");
            return new PackTriageEnrichment(runbooks, evidenceCollectors, errors);
        }

        _logger.LogDebug("Found {PackCount} Mode-A packs for triage enrichment", packs.Count);

        foreach (var pack in packs)
        {
            if (!pack.Validation.IsValid)
            {
                _logger.LogDebug("Skipping invalid pack {PackName}", pack.Manifest.Name);
                continue;
            }

            await CollectRunbooksAsync(pack, runbooks, errors, ct);
            await CollectEvidenceCollectorsAsync(pack, evidenceCollectors, errors, ct);
        }

        _logger.LogDebug(
            "Triage enrichment complete: {RunbookCount} runbooks, {EcCount} evidence collectors, {ErrorCount} errors",
            runbooks.Count,
            evidenceCollectors.Count,
            errors.Count);

        return new PackTriageEnrichment(runbooks, evidenceCollectors, errors);
    }

    private async Task CollectRunbooksAsync(
        Domain.Models.LoadedPack pack,
        List<PackRunbookDetail> runbooks,
        List<string> errors,
        CancellationToken ct)
    {
        foreach (var runbook in pack.Manifest.Runbooks)
        {
            try
            {
                var content = await _fileReader.ReadFileAsync(pack.PackPath, runbook.File, ct);

                var snippet = content is not null && content.Length > MaxSnippetLength
                    ? content[..MaxSnippetLength]
                    : content;

                runbooks.Add(new PackRunbookDetail(
                    pack.Manifest.Name,
                    runbook.Id,
                    runbook.File,
                    snippet));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to read runbook {RunbookId} from pack {PackName}",
                    runbook.Id,
                    pack.Manifest.Name);
                errors.Add($"Pack '{pack.Manifest.Name}' runbook '{runbook.Id}': {ex.Message}");
            }
        }
    }

    private async Task CollectEvidenceCollectorsAsync(
        Domain.Models.LoadedPack pack,
        List<PackEvidenceCollectorDetail> evidenceCollectors,
        List<string> errors,
        CancellationToken ct)
    {
        foreach (var ec in pack.Manifest.EvidenceCollectors)
        {
            // Only collect Mode-A evidence collectors in this enricher
            if (!string.Equals(ec.RequiredMode, ModeA, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string? kqlContent = null;
                if (!string.IsNullOrWhiteSpace(ec.QueryFile))
                {
                    kqlContent = await _fileReader.ReadFileAsync(pack.PackPath, ec.QueryFile, ct);
                }

                evidenceCollectors.Add(new PackEvidenceCollectorDetail(
                    pack.Manifest.Name,
                    ec.Id,
                    ec.RequiredMode,
                    ec.QueryFile,
                    kqlContent));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to read evidence collector {EcId} from pack {PackName}",
                    ec.Id,
                    pack.Manifest.Name);
                errors.Add($"Pack '{pack.Manifest.Name}' evidence collector '{ec.Id}': {ex.Message}");
            }
        }
    }
}
