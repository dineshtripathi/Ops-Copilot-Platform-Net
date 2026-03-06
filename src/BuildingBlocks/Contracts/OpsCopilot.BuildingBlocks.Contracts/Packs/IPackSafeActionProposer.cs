namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Discovers Mode-B+ safe actions from pack manifests and definition files,
/// returning them as proposals (recommendations only — no execution).
/// </summary>
public interface IPackSafeActionProposer
{
    /// <summary>
    /// Discovers and returns eligible safe-action proposals for the given deployment mode.
    /// Double-gated: returns empty when mode &lt; B <em>or</em> safe-actions are disabled in config.
    /// </summary>
    Task<PackSafeActionProposalResult> ProposeAsync(
        PackSafeActionProposalRequest request,
        CancellationToken ct = default);
}
