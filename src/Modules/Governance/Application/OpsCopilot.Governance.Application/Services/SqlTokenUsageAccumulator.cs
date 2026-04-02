using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Governance.Application.Services;

/// <summary>
/// SQL-backed token usage accumulator. Delegates session totals to
/// <see cref="ISessionTokenQuery"/>, which reads from the persisted
/// <c>AgentRun.TotalTokens</c> columns via the AgentRuns module.
///
/// <para><see cref="AddTokens"/> is intentionally a no-op: token totals are
/// already persisted by <c>AgentRun.SetLedgerMetadata</c> during run completion.
/// The endpoint calls <c>AddTokens</c> as a post-run notification; with durable
/// storage, that data already exists and no additional accumulation is needed.</para>
/// </summary>
public sealed class SqlTokenUsageAccumulator : ITokenUsageAccumulator
{
    private readonly ISessionTokenQuery _sessionTokenQuery;

    public SqlTokenUsageAccumulator(ISessionTokenQuery sessionTokenQuery)
        => _sessionTokenQuery = sessionTokenQuery;

    /// <inheritdoc />
    /// <remarks>No-op — token persistence is handled by <c>AgentRun.SetLedgerMetadata</c>.</remarks>
    public void AddTokens(string tenantId, string sessionId, int tokens) { }

    /// <inheritdoc />
    public int GetTotalTokens(string tenantId, string sessionId)
        => _sessionTokenQuery.GetSessionTokenTotal(tenantId, sessionId);
}
