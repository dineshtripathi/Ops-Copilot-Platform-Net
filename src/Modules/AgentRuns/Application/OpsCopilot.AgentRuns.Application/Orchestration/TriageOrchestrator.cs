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
    private readonly IRunbookSearchToolClient _runbook;
    private readonly ILogger<TriageOrchestrator> _log;
    private readonly IToolAllowlistPolicy  _allowlist;
    private readonly ITokenBudgetPolicy    _budget;
    private readonly IDegradedModePolicy   _degraded;
    private readonly ISessionStore         _sessionStore;
    private readonly ISessionPolicy        _sessionPolicy;
    private readonly TimeProvider           _timeProvider;

    private const string ToolName        = "kql_query";
    private const string RunbookToolName = "runbook_search";
    private const int    MaxPriorRuns    = 5;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public TriageOrchestrator(
        IAgentRunRepository repo,
        IKqlToolClient kql,
        IRunbookSearchToolClient runbook,
        ILogger<TriageOrchestrator> log,
        IToolAllowlistPolicy allowlist,
        ITokenBudgetPolicy budget,
        IDegradedModePolicy degraded,
        ISessionStore sessionStore,
        ISessionPolicy sessionPolicy,
        TimeProvider timeProvider)
    {
        _repo          = repo;
        _kql           = kql;
        _runbook       = runbook;
        _log           = log;
        _allowlist     = allowlist;
        _budget        = budget;
        _degraded      = degraded;
        _sessionStore  = sessionStore;
        _sessionPolicy = sessionPolicy;
        _timeProvider  = timeProvider;
    }

    public async Task<TriageResult> RunAsync(
        string tenantId,
        string alertFingerprint,
        string workspaceId,
        int    timeRangeMinutes = 120,
        string? alertTitle = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        _log.LogInformation("Triage run starting for tenant {TenantId}, fingerprint {Fingerprint}",
            tenantId, alertFingerprint);

        // ── Session resolution ──────────────────────────────────────
        var ttl = _sessionPolicy.GetSessionTtl(tenantId);
        SessionInfo session;
        SessionContext? sessionContext = null;
        bool usedSessionContext = false;
        string sessionReasonCode;
        string sessionMessage;

        if (sessionId is not null)
        {
            var existing = await _sessionStore.GetIncludingExpiredAsync(sessionId.Value, ct);
            if (existing is not null)
            {
                if (existing.TenantId != tenantId)
                {
                    // Tenant mismatch — hard reject, no run created
                    _log.LogWarning("Session {SessionId} owned by tenant {OwnerTenant} but caller is {CallerTenant}; rejecting",
                        sessionId.Value, existing.TenantId, tenantId);
                    throw new SessionTenantMismatchException(sessionId.Value, existing.TenantId, tenantId);
                }

                if (existing.ExpiresAtUtc > _timeProvider.GetUtcNow())
                {
                    // Valid, unexpired session — resume
                    await _sessionStore.TouchAsync(existing.SessionId, ttl, ct);
                    session = existing with { IsNew = false };

                    var priorRuns = await _repo.GetRecentRunsBySessionAsync(existing.SessionId, MaxPriorRuns, ct);
                    if (priorRuns.Count > 0)
                    {
                        sessionContext = new SessionContext(
                            priorRuns.Select(r => new PriorRunSummary(
                                r.RunId,
                                r.Status,
                                r.AlertFingerprint,
                                ParseCitationCount(r.CitationsJson),
                                0, // RunbookCitationCount not stored on AgentRun — MVP-safe default
                                r.CreatedAtUtc)).ToList());
                        usedSessionContext = true;
                    }

                    _log.LogInformation("Resuming session {SessionId} with {PriorRunCount} prior runs",
                        existing.SessionId, priorRuns.Count);
                    sessionReasonCode = "SessionResumed";
                    sessionMessage = $"Resumed session {existing.SessionId} with {priorRuns.Count} prior runs";
                }
                else
                {
                    // Expired — create new session
                    _log.LogWarning("Session {SessionId} expired at {ExpiresAt}; creating new session",
                        sessionId.Value, existing.ExpiresAtUtc);
                    session = await _sessionStore.CreateAsync(tenantId, ttl, ct);
                    sessionReasonCode = "SessionExpiredFallback";
                    sessionMessage = $"Session {sessionId.Value} expired at {existing.ExpiresAtUtc}; created new session {session.SessionId}";
                }
            }
            else
            {
                // Session ID never existed
                _log.LogWarning("Session {SessionId} not found; creating new session", sessionId.Value);
                session = await _sessionStore.CreateAsync(tenantId, ttl, ct);
                sessionReasonCode = "SessionNotFoundFallback";
                sessionMessage = $"Session {sessionId.Value} not found; created new session {session.SessionId}";
            }
        }
        else
        {
            session = await _sessionStore.CreateAsync(tenantId, ttl, ct);
            sessionReasonCode = "SessionCreated";
            sessionMessage = $"Created new session {session.SessionId}";
        }

        var run = await _repo.CreateRunAsync(tenantId, alertFingerprint, session.SessionId, ct);

        // ── Session lifecycle audit event ────────────────────────────
        await _repo.AppendPolicyEventAsync(
            AgentRunPolicyEvent.Create(run.RunId, "SessionPolicy",
                true, sessionReasonCode, sessionMessage), ct);

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
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>(), Array.Empty<RunbookCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext);
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
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>(), Array.Empty<RunbookCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext);
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

            return new TriageResult(run.RunId, mappedStatus, null, new[] { citation }, Array.Empty<RunbookCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext);
        }

        sw.Stop();

        var toolCitation      = BuildCitation(response);
        var toolCitationsJson = JsonSerializer.Serialize(new[] { toolCitation }, JsonOpts);

        await _repo.AppendToolCallAsync(
            ToolCall.Create(run.RunId, ToolName,
                JsonSerializer.Serialize(request, JsonOpts),
                JsonSerializer.Serialize(response, JsonOpts),
                toolStatus, sw.ElapsedMilliseconds, toolCitationsJson), ct);

        // ── Runbook search (partial degradation — failure here does NOT fail the run) ──
        var runbookCitations = new List<RunbookCitation>();

        var rbAllowlist = _allowlist.CanUseTool(tenantId, RunbookToolName);
        await _repo.AppendPolicyEventAsync(
            AgentRunPolicyEvent.Create(run.RunId, nameof(IToolAllowlistPolicy),
                rbAllowlist.Allowed, rbAllowlist.ReasonCode, rbAllowlist.Message), ct);

        if (rbAllowlist.Allowed)
        {
            var rbBudget = _budget.CheckRunBudget(tenantId, run.RunId);
            await _repo.AppendPolicyEventAsync(
                AgentRunPolicyEvent.Create(run.RunId, nameof(ITokenBudgetPolicy),
                    rbBudget.Allowed, rbBudget.ReasonCode, rbBudget.Message), ct);

            if (rbBudget.Allowed)
            {
                var rbSw = Stopwatch.StartNew();
                try
                {
                    var rbRequest  = new RunbookSearchToolRequest(alertTitle ?? alertFingerprint);
                    var rbResponse = await _runbook.ExecuteAsync(rbRequest, ct);
                    rbSw.Stop();

                    var rbStatus = rbResponse.Ok ? "Success" : "Failed";

                    if (rbResponse.Ok)
                    {
                        foreach (var hit in rbResponse.Hits)
                            runbookCitations.Add(new RunbookCitation(hit.RunbookId, hit.Title, hit.Snippet, hit.Score));
                    }

                    await _repo.AppendToolCallAsync(
                        ToolCall.Create(run.RunId, RunbookToolName,
                            JsonSerializer.Serialize(rbRequest, JsonOpts),
                            JsonSerializer.Serialize(rbResponse, JsonOpts),
                            rbStatus, rbSw.ElapsedMilliseconds,
                            JsonSerializer.Serialize(runbookCitations, JsonOpts)), ct);
                }
                catch (Exception rbEx)
                {
                    rbSw.Stop();
                    _log.LogWarning(rbEx, "Runbook search failed for run {RunId}, continuing with partial results", run.RunId);

                    var rbDeg = _degraded.MapFailure(rbEx);
                    await _repo.AppendPolicyEventAsync(
                        AgentRunPolicyEvent.Create(run.RunId, nameof(IDegradedModePolicy),
                            !rbDeg.IsDegraded, rbDeg.ErrorCode, rbDeg.UserMessage), ct);

                    await _repo.AppendToolCallAsync(
                        ToolCall.Create(run.RunId, RunbookToolName,
                            JsonSerializer.Serialize(new { query = alertFingerprint }, JsonOpts),
                            JsonSerializer.Serialize(new { error = rbEx.Message }, JsonOpts),
                            "Failed", rbSw.ElapsedMilliseconds, "[]"), ct);
                }
            }
        }

        var finalStatus = response.Ok ? AgentRunStatus.Completed : AgentRunStatus.Degraded;
        var summaryJson = response.Ok
            ? JsonSerializer.Serialize(new { rowCount = response.Rows.Count, runbookHits = runbookCitations.Count }, JsonOpts)
            : JsonSerializer.Serialize(new { error = response.Error }, JsonOpts);

        await _repo.CompleteRunAsync(run.RunId, finalStatus, summaryJson, toolCitationsJson, ct);

        _log.LogInformation("Triage run {RunId} completed with status {Status} in {ElapsedMs}ms",
            run.RunId, finalStatus, sw.ElapsedMilliseconds);

        return new TriageResult(run.RunId, finalStatus, summaryJson, new[] { toolCitation }, runbookCitations, session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext);
    }

    private static KqlCitation BuildCitation(KqlToolResponse r)
        => new(r.WorkspaceId, r.ExecutedQuery, r.Timespan, r.ExecutedAtUtc);

    private static int ParseCitationCount(string? citationsJson)
    {
        if (string.IsNullOrWhiteSpace(citationsJson))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(citationsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}


