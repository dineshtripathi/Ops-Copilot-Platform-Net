using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Models;
using OpsCopilot.Prompting.Domain.Entities;

namespace OpsCopilot.Prompting.Application.Services;

/// <summary>
/// Decorator over <see cref="IPromptRegistry"/> that routes a configurable
/// percentage of requests to a candidate prompt version (canary traffic split).
/// When no canary is active for the requested key, delegates directly to the inner registry.
/// </summary>
internal sealed class CanaryPromptStrategy : IPromptRegistry
{
    private readonly IPromptRegistry _inner;
    private readonly ICanaryStore _store;

    public CanaryPromptStrategy(IPromptRegistry inner, ICanaryStore store)
    {
        _inner = inner;
        _store = store;
    }

    public async Task<PromptTemplate?> ResolveAsync(string promptKey, CancellationToken ct = default)
    {
        var canary = _store.GetCanary(promptKey);
        if (canary is null)
            return await _inner.ResolveAsync(promptKey, ct);

        // Probabilistic traffic split: Random.Shared is thread-safe.
        var bucket = Random.Shared.Next(100);
        if (bucket < canary.TrafficPercent)
            return PromptTemplate.Create(promptKey, canary.CandidateContent, canary.CandidateVersion);

        return await _inner.ResolveAsync(promptKey, ct);
    }
}
