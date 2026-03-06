namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Executes Mode-B (and above) read-only evidence collectors via observability connectors.
/// Returns collected evidence items suitable for inclusion in a triage response.
/// </summary>
public interface IPackEvidenceExecutor
{
    /// <summary>
    /// Discovers and runs eligible evidence collectors for the given deployment mode.
    /// Double-gated: returns empty when mode &lt; B <em>or</em> evidence execution is disabled in config.
    /// </summary>
    Task<PackEvidenceExecutionResult> ExecuteAsync(
        PackEvidenceExecutionRequest request,
        CancellationToken ct = default);
}
