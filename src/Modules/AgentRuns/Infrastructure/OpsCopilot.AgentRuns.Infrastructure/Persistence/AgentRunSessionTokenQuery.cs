using Microsoft.EntityFrameworkCore;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence;

/// <summary>
/// Queries the cumulative token total for a session directly from the AgentRuns SQL table.
/// Sums <c>TotalTokens</c> across all completed runs for a given tenant + session GUID.
/// </summary>
internal sealed class AgentRunSessionTokenQuery : ISessionTokenQuery
{
    private readonly AgentRunsDbContext _db;

    public AgentRunSessionTokenQuery(AgentRunsDbContext db) => _db = db;

    /// <inheritdoc />
    public int GetSessionTokenTotal(string tenantId, string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
            return 0;

        return _db.AgentRuns
            .Where(r => r.TenantId == tenantId
                     && r.SessionId == sessionGuid
                     && r.TotalTokens.HasValue)
            .Sum(r => r.TotalTokens ?? 0);
    }
}
