using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.BuildingBlocks.Contracts.Packs;

namespace OpsCopilot.AgentRuns.Infrastructure.Memory;

/// <summary>
/// KQL-backed implementation of <see cref="IIncidentMemoryService"/> that executes
/// all Mode-B evidence packs against Azure Monitor / Log Analytics for the tenant.
/// Used as a fallback when <see cref="SqlIncidentMemoryService"/> returns no results
/// (e.g. first-run or after a DB refresh).
/// </summary>
internal sealed class LiveKqlIncidentMemoryService : IIncidentMemoryService
{
    private readonly IPackEvidenceExecutor                   _executor;
    private readonly ILogger<LiveKqlIncidentMemoryService>   _log;

    public LiveKqlIncidentMemoryService(
        IPackEvidenceExecutor                   executor,
        ILogger<LiveKqlIncidentMemoryService>   log)
    {
        _executor = executor;
        _log      = log;
    }

    public async Task<IReadOnlyList<MemoryCitation>> RecallAsync(
        string            alertFingerprint,
        string            tenantId,
        CancellationToken ct = default)
    {
        try
        {
            var request = new PackEvidenceExecutionRequest("B", tenantId);
            var result  = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);

            var citations = new List<MemoryCitation>();

            foreach (var item in result.EvidenceItems)
            {
                if (item.RowCount <= 0 || item.ErrorMessage is not null)
                    continue;

                citations.Add(new MemoryCitation(
                    RunId:            $"kql/{item.PackName}/{item.CollectorId}",
                    AlertFingerprint: $"{item.PackName}.{item.CollectorId}",
                    SummarySnippet:   $"{item.RowCount} result(s) from '{item.CollectorId}' ({item.PackName} pack) — live Azure Monitor evidence",
                    Score:            0.0,
                    CreatedAtUtc:     DateTimeOffset.UtcNow));
            }

            _log.LogDebug(
                "LiveKqlIncidentMemoryService: {Count} citation(s) from {PackCount} pack item(s) for tenant {TenantId}",
                citations.Count, result.EvidenceItems.Count, tenantId);

            return citations;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "LiveKqlIncidentMemoryService: pack execution failed for tenant {TenantId}; returning empty citations",
                tenantId);
            return [];
        }
    }
}
