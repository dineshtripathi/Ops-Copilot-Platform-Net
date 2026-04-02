namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Provides session-scoped token totals from durable storage.
/// Implemented by AgentRuns infrastructure — resolves session totals
/// from persisted <see cref="AgentRun.TotalTokens"/> columns.
/// </summary>
public interface ISessionTokenQuery
{
    /// <summary>
    /// Returns the cumulative tokens consumed across all completed runs in the given session.
    /// Returns 0 if the session has no runs or no token data is available.
    /// </summary>
    int GetSessionTokenTotal(string tenantId, string sessionId);
}
