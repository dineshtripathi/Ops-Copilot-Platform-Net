namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>Resolves the current active prompt version for a named prompt key.</summary>
public interface IPromptVersionService
{
    Task<PromptVersionInfo> GetCurrentVersionAsync(string promptKey, CancellationToken ct = default);
}
