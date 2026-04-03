namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Tracks per-session token consumption for budget enforcement.
/// </summary>
public interface ITokenUsageAccumulator
{
    /// <summary>Adds tokens consumed by a single run to the session total.</summary>
    void AddTokens(string tenantId, string sessionId, int tokens);

    /// <summary>Returns the cumulative token total for the given tenant+session.</summary>
    int GetTotalTokens(string tenantId, string sessionId);
}
