using Microsoft.Extensions.Configuration;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.Routing;

internal sealed class ConfigPromptVersionService(IConfiguration config) : IPromptVersionService
{
    public Task<PromptVersionInfo> GetCurrentVersionAsync(string promptKey, CancellationToken ct = default)
    {
        var versionId = config[$"Prompting:{promptKey}:VersionId"]   ?? "0.0.0";
        var prompt    = config[$"Prompting:{promptKey}:SystemPrompt"] ?? "Analyze the following triage data.";
        return Task.FromResult(new PromptVersionInfo(versionId, prompt));
    }
}
