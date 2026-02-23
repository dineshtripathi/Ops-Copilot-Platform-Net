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
}
