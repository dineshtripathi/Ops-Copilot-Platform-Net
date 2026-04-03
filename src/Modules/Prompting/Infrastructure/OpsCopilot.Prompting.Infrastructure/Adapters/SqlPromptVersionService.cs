using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.Prompting.Application.Abstractions;

namespace OpsCopilot.Prompting.Infrastructure.Adapters;

/// <summary>
/// Implements <see cref="IPromptVersionService"/> (AgentRuns contract) by delegating
/// to the SQL-backed <see cref="IPromptRegistry"/>.
/// Falls back to a stable default when no template has been seeded.
/// </summary>
internal sealed class SqlPromptVersionService(IPromptRegistry registry) : IPromptVersionService
{
    // Fallback values when no active template row exists for the key.
    private static readonly Dictionary<string, string> DefaultPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["triage"] = "You are an expert SRE. Analyze the following triage data and summarise the incident, likely cause, and recommended remediation steps.",
        ["chat"]   = "You are an incident-response assistant for OpsCopilot. Answer the operator's question concisely using the context below if relevant.",
    };

    public async Task<PromptVersionInfo> GetCurrentVersionAsync(string promptKey, CancellationToken ct = default)
    {
        var template = await registry.ResolveAsync(promptKey, ct);
        if (template is not null)
            return new PromptVersionInfo($"v{template.Version}", template.Content);

        var fallback = DefaultPrompts.TryGetValue(promptKey, out var def)
            ? def
            : "Analyze the following data and provide a concise summary.";

        return new PromptVersionInfo("v1-default", fallback);
    }
}
