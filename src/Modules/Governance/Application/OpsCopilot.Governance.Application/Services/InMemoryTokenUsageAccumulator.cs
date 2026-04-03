using System.Collections.Concurrent;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Governance.Application.Services;

/// <summary>
/// Thread-safe in-memory accumulator for per-session token usage tracking.
/// Registered as a singleton so counts persist across scoped requests.
/// </summary>
public sealed class InMemoryTokenUsageAccumulator : ITokenUsageAccumulator
{
    private readonly ConcurrentDictionary<string, int> _store = new();

    public void AddTokens(string tenantId, string sessionId, int tokens)
    {
        var key = $"{tenantId}:{sessionId}";
        _store.AddOrUpdate(key, tokens, (_, existing) => existing + tokens);
    }

    public int GetTotalTokens(string tenantId, string sessionId)
    {
        var key = $"{tenantId}:{sessionId}";
        return _store.TryGetValue(key, out var total) ? total : 0;
    }
}
