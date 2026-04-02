using System.Collections.Concurrent;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Models;

namespace OpsCopilot.Prompting.Application.Services;

/// <summary>
/// Thread-safe, in-memory store for active canary experiments.
/// Registered as singleton so state is shared across scoped requests.
/// </summary>
internal sealed class InMemoryCanaryStore : ICanaryStore
{
    private readonly ConcurrentDictionary<string, CanaryState> _canaries = new(StringComparer.OrdinalIgnoreCase);

    public CanaryState? GetCanary(string promptKey)
        => _canaries.GetValueOrDefault(promptKey);

    public void SetCanary(string promptKey, CanaryState state)
        => _canaries[promptKey] = state;

    public void RemoveCanary(string promptKey)
        => _canaries.TryRemove(promptKey, out _);
}
