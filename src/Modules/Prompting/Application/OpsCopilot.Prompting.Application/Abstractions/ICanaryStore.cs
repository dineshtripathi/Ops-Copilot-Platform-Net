using OpsCopilot.Prompting.Application.Models;

namespace OpsCopilot.Prompting.Application.Abstractions;

/// <summary>
/// Thread-safe store for active canary experiments.
/// Each prompt key can have at most one active canary at a time.
/// </summary>
public interface ICanaryStore
{
    CanaryState? GetCanary(string promptKey);
    void SetCanary(string promptKey, CanaryState state);
    void RemoveCanary(string promptKey);
}
