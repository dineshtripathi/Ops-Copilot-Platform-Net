using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.BuildingBlocks.Contracts.Rag;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Handles conversational Q&amp;A requests from operators.
/// Retrieves memory citations and runbook context, then calls the LLM to generate an answer.
/// </summary>
public sealed class ChatOrchestrator
{
    private readonly IChatClient?             _chatClient;
    private readonly IIncidentMemoryService   _memory;
    private readonly IRunbookSearchToolClient _runbook;
    private readonly IRunbookAclFilter        _aclFilter;
    private readonly ILogger<ChatOrchestrator> _log;
    private readonly IPromptVersionService?   _promptVersion;

    public ChatOrchestrator(
        IIncidentMemoryService    memory,
        IRunbookSearchToolClient  runbook,
        IRunbookAclFilter         aclFilter,
        ILogger<ChatOrchestrator> log,
        IChatClient?              chatClient    = null,
        IPromptVersionService?    promptVersion = null)
    {
        _chatClient    = chatClient;
        _memory        = memory;
        _runbook       = runbook;
        _aclFilter     = aclFilter;
        _log           = log;
        _promptVersion = promptVersion;
    }

    /// <summary>
    /// Answers the operator's <paramref name="query"/> using incident memory and runbook context.
    /// Never throws — returns a degraded answer string on LLM failure.
    /// </summary>
    public async Task<ChatResult> ChatAsync(
        string            tenantId,
        string            query,
        CancellationToken cancellationToken = default)
    {
        var memCitations    = await _memory.RecallAsync(query, tenantId, cancellationToken);
        var runbookResponse = await _runbook.ExecuteAsync(
            new RunbookSearchToolRequest(query, MaxResults: 5), cancellationToken);

        var callerContext    = RunbookCallerContext.TenantOnly(tenantId);
        var filtered         = _aclFilter.Filter(runbookResponse.Hits, callerContext);
        var runbookCitations = filtered
            .Select(h => new RunbookCitation(h.RunbookId, h.Title, h.Snippet, h.Score))
            .ToList();

        var versionInfo = await (_promptVersion?.GetCurrentVersionAsync("chat", cancellationToken)
            ?? Task.FromResult(new PromptVersionInfo("v1-default",
                "You are an incident-response assistant for OpsCopilot. Answer the operator's question concisely using the context below if relevant.")));

        var systemPrompt = BuildSystemPrompt(versionInfo.SystemPrompt, memCitations, runbookCitations);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User,   query),
        };

        string answer;
        if (_chatClient is null)
        {
            answer = "Chat is not available — LLM provider is not configured. Contact your platform administrator.";
        }
        else
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
                answer = response.Text ?? string.Empty;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LLM chat call failed for tenant {TenantId}", tenantId);
                answer = "Unable to generate a response at this time. Please try again later.";
            }
        }

        return new ChatResult(answer, memCitations, runbookCitations);
    }

    private static string BuildSystemPrompt(
        string                         basePrompt,
        IReadOnlyList<MemoryCitation>  memoryCitations,
        IReadOnlyList<RunbookCitation> runbookCitations)
    {
        var sb = new StringBuilder();
        sb.AppendLine(basePrompt);
        sb.AppendLine();

        if (memoryCitations.Count > 0)
        {
            sb.AppendLine("## Prior incident context");
            foreach (var m in memoryCitations)
                sb.AppendLine($"- [{m.AlertFingerprint}] {m.SummarySnippet}");
            sb.AppendLine();
        }

        if (runbookCitations.Count > 0)
        {
            sb.AppendLine("## Relevant runbooks");
            foreach (var r in runbookCitations)
                sb.AppendLine($"- [{r.Title}] {r.Snippet}");
            sb.AppendLine();
        }

        if (memoryCitations.Count == 0 && runbookCitations.Count == 0)
            sb.AppendLine("No prior incident context or runbooks were found for this query. Answer from general knowledge if possible.");

        return sb.ToString();
    }
}

