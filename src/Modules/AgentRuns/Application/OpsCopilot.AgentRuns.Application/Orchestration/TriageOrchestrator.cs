using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.Governance.Application.Policies;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Core triage orchestrator with Slice 3A guardrails:
///   1. Create AgentRun (Pending)
///   2. Check tool-allowlist policy  → denied ⇒ Failed (NO MCP call)
///   3. Check token-budget policy    → denied ⇒ Failed (NO MCP call)
///   4. Build KQL → call McpHost via IKqlToolClient
///   5. On exception → apply degraded-mode policy
///   6. Persist ToolCall + PolicyEvents (INSERT-only, always)
///   7. Complete AgentRun (Completed | Degraded | Failed)
///
/// Hallucination guard: if the tool fails, status = Degraded and the error is
/// recorded in the ToolCall + CitationsJson. No fabricated data ever enters the ledger.
/// Denied paths NEVER call MCP — the run short-circuits to Failed immediately.
/// </summary>
public sealed class TriageOrchestrator
{
    private readonly IAgentRunRepository _repo;
    private readonly IKqlToolClient      _kql;
    private readonly ILogger<TriageOrchestrator> _log;
    private readonly IToolAllowlistPolicy  _allowlist;
    private readonly ITokenBudgetPolicy    _budget;
    private readonly IDegradedModePolicy   _degraded;

    private const string ToolName = "kql_query";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public TriageOrchestrator(
        IAgentRunRepository repo,
        IKqlToolClient kql,
        ILogger<TriageOrchestrator> log,
        IToolAllowlistPolicy allowlist,
        ITokenBudgetPolicy budget,
        IDegradedModePolicy degraded)
    {
        _repo      = repo;
        _kql       = kql;
        _log       = log;
        _allowlist = allowlist;
        _budget    = budget;
        _degraded  = degraded;
    }

    public async Task<TriageResult> RunAsync(
        string tenantId,
        string alertFingerprint,
        string workspaceId,
        int    timeRangeMinutes = 120,
        CancellationToken ct = default)
    {
        _log.LogInformation("Triage run starting for tenant {TenantId}, fingerprint {Fingerprint}",
            tenantId, alertFingerprint);

        var run = await _repo.CreateRunAsync(tenantId, alertFingerprint, ct);

        // ── Guardrail 1: tool allowlist ─────────────────────────────
        var allowlistDecision = _allowlist.CanUseTool(tenantId, ToolName);
        await _repo.AppendPolicyEventAsync(
            AgentRunPolicyEvent.Create(run.RunId, nameof(IToolAllowlistPolicy),
                allowlistDecision.Allowed, allowlistDecision.ReasonCode, allowlistDecision.Message), ct);

        if (!allowlistDecision.Allowed)
        {
            _log.LogWarning("Tool {Tool} denied by allowlist for run {RunId}: {Reason}",
                ToolName, run.RunId, allowlistDecision.ReasonCode);
            await _repo.CompleteRunAsync(run.RunId, AgentRunStatus.Failed,
                JsonSerializer.Serialize(new { policy = "ToolAllowlist", reason = allowlistDecision.ReasonCode }, JsonOpts),
                "[]", ct);
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>());
        }

        // ── Guardrail 2: token budget ───────────────────────────────
        var budgetDecision = _budget.CheckRunBudget(tenantId, run.RunId);
        await _repo.AppendPolicyEventAsync(
            AgentRunPolicyEvent.Create(run.RunId, nameof(ITokenBudgetPolicy),
                budgetDecision.Allowed, budgetDecision.ReasonCode, budgetDecision.Message), ct);

        if (!budgetDecision.Allowed)
        {
            _log.LogWarning("Token budget denied for run {RunId}: {Reason}",
                run.RunId, budgetDecision.ReasonCode);
            await _repo.CompleteRunAsync(run.RunId, AgentRunStatus.Failed,
                JsonSerializer.Serialize(new { policy = "TokenBudget", reason = budgetDecision.ReasonCode }, JsonOpts),
                "[]", ct);
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>());
        }

        // ── Execute KQL tool (MCP) ──────────────────────────────────
        // search * is table-agnostic — works on any Log Analytics workspace
        // regardless of whether App Insights, Container Apps, or other tables exist.
        var kql      = $"search * | where TimeGenerated > ago({timeRangeMinutes}m) | take 20";
        var timespan = $"PT{timeRangeMinutes}M";
        var request  = new KqlToolRequest(tenantId, workspaceId, kql, timespan);

        var sw = Stopwatch.StartNew();

        KqlToolResponse response;
        string          toolStatus;

        try
        {
            response   = await _kql.ExecuteAsync(request, ct);
            toolStatus = response.Ok ? "Success" : "Failed";
        }
        catch (Exception ex)
        {
            sw.Stop();

            // ── Guardrail 3: degraded-mode policy ───────────────────
            var degradedDecision = _degraded.MapFailure(ex);
            await _repo.AppendPolicyEventAsync(
                AgentRunPolicyEvent.Create(run.RunId, nameof(IDegradedModePolicy),
                    !degradedDecision.IsDegraded, degradedDecision.ErrorCode, degradedDecision.UserMessage), ct);

            var mappedStatus = degradedDecision.IsDegraded
                ? AgentRunStatus.Degraded
                : AgentRunStatus.Failed;

            // Tool threw — record degraded/failed state with full error context
            response = new KqlToolResponse(
                Ok:            false,
                Rows:          Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ExecutedQuery: kql,
                WorkspaceId:   workspaceId,
                Timespan:      timespan,
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error:         ex.Message);
            toolStatus = "Failed";

            _log.LogWarning(ex, "KQL tool threw for run {RunId}, mapped to {Status}",
                run.RunId, mappedStatus);

            var citation      = BuildCitation(response);
            var citationsJson = JsonSerializer.Serialize(new[] { citation }, JsonOpts);

            await _repo.AppendToolCallAsync(
                ToolCall.Create(run.RunId, ToolName,
                    JsonSerializer.Serialize(request, JsonOpts),
                    JsonSerializer.Serialize(response, JsonOpts),
                    toolStatus, sw.ElapsedMilliseconds, citationsJson), ct);

            await _repo.CompleteRunAsync(run.RunId, mappedStatus,
                JsonSerializer.Serialize(new { error = ex.Message, errorCode = degradedDecision.ErrorCode }, JsonOpts),
                citationsJson, ct);

            return new TriageResult(run.RunId, mappedStatus, null, new[] { citation });
        }

        sw.Stop();

        var toolCitation      = BuildCitation(response);
        var toolCitationsJson = JsonSerializer.Serialize(new[] { toolCitation }, JsonOpts);

        await _repo.AppendToolCallAsync(
            ToolCall.Create(run.RunId, ToolName,
                JsonSerializer.Serialize(request, JsonOpts),
                JsonSerializer.Serialize(response, JsonOpts),
                toolStatus, sw.ElapsedMilliseconds, toolCitationsJson), ct);

        var finalStatus = response.Ok ? AgentRunStatus.Completed : AgentRunStatus.Degraded;
        var summaryJson = response.Ok
            ? JsonSerializer.Serialize(new { rowCount = response.Rows.Count }, JsonOpts)
            : JsonSerializer.Serialize(new { error = response.Error }, JsonOpts);

        await _repo.CompleteRunAsync(run.RunId, finalStatus, summaryJson, toolCitationsJson, ct);

        _log.LogInformation("Triage run {RunId} completed with status {Status} in {ElapsedMs}ms",
            run.RunId, finalStatus, sw.ElapsedMilliseconds);

        return new TriageResult(run.RunId, finalStatus, summaryJson, new[] { toolCitation });
    }

    private static KqlCitation BuildCitation(KqlToolResponse r)
        => new(r.WorkspaceId, r.ExecutedQuery, r.Timespan, r.ExecutedAtUtc);
}


