namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Cross-module contract: enriches a triage result with Pack runbooks and evidence collectors.
/// Implementations live in the Packs module; consumers live in AgentRuns.Presentation.
/// </summary>
public interface IPackTriageEnricher
{
    /// <summary>
    /// Discovers Mode-A pack runbooks and evidence collectors, reads their file contents,
    /// and returns an enrichment payload to be appended to the triage response.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<PackTriageEnrichment> EnrichAsync(CancellationToken ct = default);
}
