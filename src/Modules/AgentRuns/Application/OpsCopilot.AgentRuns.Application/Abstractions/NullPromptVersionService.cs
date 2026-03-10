namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Fallback prompt version service — returns a versioned no-op system prompt.
/// Registered automatically when no real IPromptVersionService is wired.
/// </summary>
internal sealed class NullPromptVersionService : IPromptVersionService
{
    public Task<PromptVersionInfo> GetCurrentVersionAsync(string promptKey, CancellationToken ct = default)
        => Task.FromResult(new PromptVersionInfo("0.0.0", "Analyze the following triage data."));
}
