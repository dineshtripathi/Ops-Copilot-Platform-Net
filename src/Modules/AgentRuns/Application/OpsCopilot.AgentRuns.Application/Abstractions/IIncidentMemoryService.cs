namespace OpsCopilot.AgentRuns.Application.Abstractions;

public interface IIncidentMemoryService
{
    Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string            alertFingerprint,
        string            tenantId,
        CancellationToken cancellationToken = default);
}
