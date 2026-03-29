namespace OpsCopilot.BuildingBlocks.Contracts.AgentRuns;

/// <summary>
/// Narrow contract for cross-module agent-run creation.
/// AlertIngestion depends on this instead of the full IAgentRunRepository.
/// </summary>
public interface IAgentRunCreator
{
    Task<Guid> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        CancellationToken ct = default);

    Task<Guid> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        AlertRunContext? context = null,
        CancellationToken ct = default);

    /// <summary>
    /// Slice 131: Returns the SessionId of the most recent run for the given
    /// (tenantId, fingerprint) pair within <paramref name="windowMinutes"/>,
    /// or <c>null</c> when no qualifying run exists.
    /// Used by alert ingestion to continue an existing session for repeated alerts.
    /// </summary>
    Task<Guid?> FindRecentSessionIdAsync(
        string tenantId,
        string alertFingerprint,
        int windowMinutes,
        CancellationToken ct = default);
}
