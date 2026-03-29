using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsCopilot.AgentRuns.Application.Options;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Rag;

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
public sealed class TriageOrchestrator : ITriageOrchestrator
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
    private readonly TimeProvider              _timeProvider;
    private readonly IRunbookAclFilter         _aclFilter;
    private readonly ITargetScopeEvaluator?  _scopeEvaluator;

    private readonly IChatClient?           _chatClient;
    private readonly IModelRoutingPolicy?   _modelRouting;
    private readonly IPromptVersionService? _promptVersion;
    private readonly IIncidentMemoryService? _memory;
    private readonly IDeploymentDiffToolClient? _deploymentDiff;
    private readonly IIncidentMemoryIndexer?    _indexer;
    private readonly IOptions<IdempotencyOptions>? _idempotencyOptions;

    private const string ToolName        = "kql_query";
    private const string RunbookToolName = "runbook_search";
    private const string MemoryToolName        = "memory_recall";
    private const string DeploymentDiffToolName = "deployment_diff";
    private const int    MaxPriorRuns           = 5;

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
        TimeProvider timeProvider,
        IRunbookAclFilter aclFilter,
        IChatClient? chatClient = null,
        IModelRoutingPolicy? modelRouting = null,
        IPromptVersionService? promptVersion = null,
        ITargetScopeEvaluator? scopeEvaluator = null,
        IIncidentMemoryService? memory = null,
        IDeploymentDiffToolClient? deploymentDiff = null,
        IIncidentMemoryIndexer? indexer = null,
        IOptions<IdempotencyOptions>? idempotencyOptions = null)
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
        _timeProvider   = timeProvider;
        _aclFilter      = aclFilter;
        _scopeEvaluator = scopeEvaluator;
        _chatClient     = chatClient;
        _modelRouting  = modelRouting;
        _promptVersion = promptVersion;
        _memory        = memory;
        _deploymentDiff      = deploymentDiff;
        _indexer             = indexer;
        _idempotencyOptions  = idempotencyOptions;
    }

    public async Task<TriageResult> RunAsync(
        string tenantId,
        string alertFingerprint,
        string workspaceId,
        int    timeRangeMinutes = 120,
        string? alertTitle = null,
        string? subscriptionId = null,
        string? resourceGroup  = null,
        Guid? sessionId = null,
        RunContext? context = null,
        AgentRun?   existingRun = null,
        CancellationToken ct = default)
    {
        _log.LogInformation("Triage run starting for tenant {TenantId}, fingerprint {Fingerprint}",
            tenantId, alertFingerprint);

        // ── Idempotency guard (skip when resuming a dispatcher-initiated run) ─────────
        if (existingRun is null && _idempotencyOptions is not null)
        {
            var windowMinutes = _idempotencyOptions.Value.WindowMinutes;
            if (windowMinutes > 0)
            {
                var dedupRun = await _repo.FindRecentRunAsync(
                    tenantId, alertFingerprint, windowMinutes, ct);
                if (dedupRun is not null)
                {
                    _log.LogInformation(
                        "Idempotency: deduplicating run for fingerprint {Fingerprint} — reusing run {RunId} (status {Status})",
                        alertFingerprint, dedupRun.RunId, dedupRun.Status);
                    return BuildDedupResult(dedupRun);
                }
            }
        }

        // ── Session resolution ──────────────────────────────────────
        var ttl = _sessionPolicy.GetSessionTtl(tenantId);
        SessionInfo session;
        SessionContext? sessionContext = null;
        bool usedSessionContext = false;
        string sessionReasonCode;
        string sessionMessage;

        if (sessionId is not null && existingRun is null)
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
                                ParseRunbookCitationCount(r.SummaryJson),
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

        var run = existingRun
            ?? await _repo.CreateRunAsync(tenantId, alertFingerprint, session.SessionId, context, ct);

        // Slice 128: Transition Pending → Running so the status ledger reflects in-progress state.
        if (existingRun is not null)
            await _repo.MarkRunningAsync(run.RunId, ct);

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
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>(), Array.Empty<RunbookCitation>(), Array.Empty<MemoryCitation>(), Array.Empty<DeploymentDiffCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext, sessionReasonCode);
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
            return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>(), Array.Empty<RunbookCitation>(), Array.Empty<MemoryCitation>(), Array.Empty<DeploymentDiffCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext, sessionReasonCode);
        }

        // ── Guardrail 2.5: workspace scope ──────────────────────────────────
        if (_scopeEvaluator is not null)
        {
            var scopeDecision = _scopeEvaluator.Evaluate(tenantId, "log_analytics_workspace", workspaceId);
            await _repo.AppendPolicyEventAsync(
                AgentRunPolicyEvent.Create(run.RunId, nameof(ITargetScopeEvaluator),
                    scopeDecision.Allowed, scopeDecision.ReasonCode, scopeDecision.Message), ct);

            if (!scopeDecision.Allowed)
            {
                _log.LogWarning("Workspace scope denied for run {RunId}: workspace={WorkspaceId} reason={Reason}",
                    run.RunId, workspaceId, scopeDecision.ReasonCode);
                await _repo.CompleteRunAsync(run.RunId, AgentRunStatus.Failed,
                    JsonSerializer.Serialize(new { policy = "WorkspaceScope", reason = scopeDecision.ReasonCode }, JsonOpts),
                    "[]", ct);
                return new TriageResult(run.RunId, AgentRunStatus.Failed, null, Array.Empty<KqlCitation>(), Array.Empty<RunbookCitation>(), Array.Empty<MemoryCitation>(), Array.Empty<DeploymentDiffCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext, sessionReasonCode);
            }
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

            return new TriageResult(run.RunId, mappedStatus, null, new[] { citation }, Array.Empty<RunbookCitation>(), Array.Empty<MemoryCitation>(), Array.Empty<DeploymentDiffCitation>(), session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext, sessionReasonCode);
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
                        var callerCtx  = RunbookCallerContext.TenantOnly(tenantId);
                        var authorized = _aclFilter.Filter(rbResponse.Hits, callerCtx);
                        _log.LogDebug("ACL filter: {InCount} hits \u2192 {OutCount} authorized for tenant {TenantId}",
                            rbResponse.Hits.Count, authorized.Count, tenantId);
                        foreach (var hit in authorized)
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

        // ── Incident memory recall (soft error — never fails the run) ──────────────────────────
        IReadOnlyList<MemoryCitation> memoryCitations = Array.Empty<MemoryCitation>();
        if (_memory is not null && _allowlist.CanUseTool(tenantId, MemoryToolName).Allowed)
        {
            try
            {
                memoryCitations = await _memory.RecallAsync(alertFingerprint, tenantId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Incident memory recall failed for tenant {TenantId}; continuing without citations", tenantId);
            }
        }

        // ── Deployment diff (partial degradation — failure here does NOT fail the run) ────────
        // Prefer context fields (populated from alert payload) over the legacy named params.
        var effectiveSubscriptionId = subscriptionId ?? context?.AzureSubscriptionId;
        var effectiveResourceGroup  = resourceGroup  ?? context?.AzureResourceGroup;

        var deploymentDiffCitations = new List<DeploymentDiffCitation>();
        if (_deploymentDiff is not null && effectiveSubscriptionId is not null)
        {
            var ddAllowlist = _allowlist.CanUseTool(tenantId, DeploymentDiffToolName);
            await _repo.AppendPolicyEventAsync(
                AgentRunPolicyEvent.Create(run.RunId, nameof(IToolAllowlistPolicy),
                    ddAllowlist.Allowed, ddAllowlist.ReasonCode, ddAllowlist.Message), ct);

            if (ddAllowlist.Allowed)
            {
                var ddBudget = _budget.CheckRunBudget(tenantId, run.RunId);
                await _repo.AppendPolicyEventAsync(
                    AgentRunPolicyEvent.Create(run.RunId, nameof(ITokenBudgetPolicy),
                        ddBudget.Allowed, ddBudget.ReasonCode, ddBudget.Message), ct);

                if (ddBudget.Allowed)
                {
                    var ddSw = Stopwatch.StartNew();
                    try
                    {
                        var ddRequest  = new DeploymentDiffRequest(tenantId, effectiveSubscriptionId, effectiveResourceGroup, timeRangeMinutes);
                        var ddResponse = await _deploymentDiff.ExecuteAsync(ddRequest, ct);
                        ddSw.Stop();

                        if (ddResponse.Ok)
                        {
                            foreach (var change in ddResponse.Changes)
                                deploymentDiffCitations.Add(new DeploymentDiffCitation(
                                    ddResponse.SubscriptionId, change.ResourceGroup, change.ResourceId,
                                    change.ChangeType, change.ChangeTime, change.Summary));
                        }

                        await _repo.AppendToolCallAsync(
                            ToolCall.Create(run.RunId, DeploymentDiffToolName,
                                JsonSerializer.Serialize(ddRequest, JsonOpts),
                                JsonSerializer.Serialize(ddResponse, JsonOpts),
                                ddResponse.Ok ? "Success" : "Failed",
                                ddSw.ElapsedMilliseconds,
                                JsonSerializer.Serialize(deploymentDiffCitations, JsonOpts)), ct);
                    }
                    catch (Exception ddEx)
                    {
                        ddSw.Stop();
                        _log.LogWarning(ddEx, "Deployment diff failed for run {RunId}, continuing with partial results", run.RunId);

                        var ddDeg = _degraded.MapFailure(ddEx);
                        await _repo.AppendPolicyEventAsync(
                            AgentRunPolicyEvent.Create(run.RunId, nameof(IDegradedModePolicy),
                                !ddDeg.IsDegraded, ddDeg.ErrorCode, ddDeg.UserMessage), ct);

                        await _repo.AppendToolCallAsync(
                            ToolCall.Create(run.RunId, DeploymentDiffToolName,
                                "{}", "{}", "Failed", ddSw.ElapsedMilliseconds, "[]"), ct);
                    }
                }
            }
        }

        // ── LLM enrichment (runs BEFORE CompleteRunAsync so exceptions can set Degraded) ──────
        string?  modelId         = null;
        string?  promptVersionId = null;
        int?     inputTokens     = null;
        int?     outputTokens    = null;
        int?     totalTokens     = null;
        decimal? estimatedCost   = null;
        string?  llmNarrative    = null;

        var finalStatus = response.Ok ? AgentRunStatus.Completed : AgentRunStatus.Degraded;
        var summaryJson = response.Ok
            ? JsonSerializer.Serialize(new { rowCount = response.Rows.Count, runbookHits = runbookCitations.Count, memoryHits = memoryCitations.Count, diffHits = deploymentDiffCitations.Count }, JsonOpts)
            : JsonSerializer.Serialize(new { error = response.Error }, JsonOpts);

        if (_chatClient != null)
        {
            try
            {
                var descriptor  = await (_modelRouting?.SelectModelAsync(tenantId, ct)
                                         ?? Task.FromResult(new ModelDescriptor("default")));
                var versionInfo = await (_promptVersion?.GetCurrentVersionAsync("triage", ct)
                                         ?? Task.FromResult(new PromptVersionInfo("0.0.0", "Analyze the following triage data.")));

                // Build a rich evidence payload (capped to avoid context window overflow).
                const int MaxSampleRows = 20;
                var richPayload = JsonSerializer.Serialize(new
                {
                    alertFingerprint,
                    alertTitle,
                    subscriptionId,
                    resourceGroup,
                    timeRangeMinutes,
                    kqlResults = new
                    {
                        rowCount   = response.Rows.Count,
                        sampleRows = response.Rows.Take(MaxSampleRows)
                    },
                    runbooks = runbookCitations
                        .Select(r => new { r.Title, r.Snippet })
                        .ToList(),
                    memoryHits  = memoryCitations.Count,
                    diffHits    = deploymentDiffCitations.Count
                }, JsonOpts);

                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, versionInfo.SystemPrompt),
                    new(ChatRole.User,   richPayload)
                };
                var completion  = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
                llmNarrative    = completion.Text;
                var usage       = completion.Usage;
                modelId         = descriptor.ModelId;
                promptVersionId = versionInfo.VersionId;
                inputTokens     = (int?)usage?.InputTokenCount;
                outputTokens    = (int?)usage?.OutputTokenCount;
                totalTokens     = (int?)usage?.TotalTokenCount;
                estimatedCost   = ModelCostEstimator.Estimate(modelId, inputTokens ?? 0, outputTokens ?? 0);
            }
            catch (Exception llmEx)
            {
                _log.LogWarning(llmEx, "LLM analysis failed for run {RunId}, marking Degraded", run.RunId);
                finalStatus = AgentRunStatus.Degraded;
            }
        }

        await _repo.CompleteRunAsync(run.RunId, finalStatus, summaryJson!, toolCitationsJson, ct);

        // ── Fire-and-forget incident memory indexing ────────────────────────
        if (_indexer is not null && summaryJson is not null && finalStatus == AgentRunStatus.Completed)
        {
            var capturedSummary     = summaryJson;
            var capturedFingerprint = alertFingerprint;
            var capturedTenantId    = tenantId;
            var capturedRunId       = run.RunId;
            var capturedTime        = _timeProvider.GetUtcNow();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _indexer.IndexAsync(
                        capturedRunId, capturedTenantId, capturedFingerprint,
                        capturedSummary, capturedTime, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Incident memory indexing failed for run {RunId}", capturedRunId);
                }
            });
        }

        if (modelId is not null)
            await _repo.UpdateRunLedgerAsync(
                run.RunId, modelId, promptVersionId,
                inputTokens ?? 0, outputTokens ?? 0, totalTokens ?? 0, estimatedCost ?? 0m, ct);

        _log.LogInformation("Triage run {RunId} completed with status {Status} in {ElapsedMs}ms",
            run.RunId, finalStatus, sw.ElapsedMilliseconds);

        return new TriageResult(
            run.RunId, finalStatus, summaryJson, new[] { toolCitation }, runbookCitations,
            memoryCitations, deploymentDiffCitations,
            session.SessionId, session.IsNew, session.ExpiresAtUtc, usedSessionContext, sessionReasonCode,
            ModelId: modelId, PromptVersionId: promptVersionId,
            InputTokens: inputTokens, OutputTokens: outputTokens,
            TotalTokens: totalTokens, EstimatedCost: estimatedCost,
            LlmNarrative: llmNarrative);
    }

    /// <summary>
    /// Slice 127: Applies the full triage pipeline to an existing Pending <see cref="AgentRun"/>
    /// created by alert ingestion. Skips idempotency guard and run creation so the
    /// caller's <paramref name="existingRun.RunId"/> is preserved end-to-end.
    /// </summary>
    public Task<TriageResult> ResumeRunAsync(
        AgentRun  existingRun,
        string    workspaceId,
        int       timeRangeMinutes = 120,
        string?   alertTitle = null,
        CancellationToken ct = default)
        => RunAsync(
            existingRun.TenantId,
            existingRun.AlertFingerprint ?? string.Empty,
            workspaceId,
            timeRangeMinutes,
            alertTitle,
            subscriptionId: existingRun.AzureSubscriptionId,
            resourceGroup:  existingRun.AzureResourceGroup,
            existingRun:    existingRun,
            ct:             ct);

    private static TriageResult BuildDedupResult(AgentRun existing)
        => new(
            existing.RunId,
            existing.Status,
            SummaryJson:             null,
            Citations:               Array.Empty<KqlCitation>(),
            RunbookCitations:        Array.Empty<RunbookCitation>(),
            MemoryCitations:         Array.Empty<MemoryCitation>(),
            DeploymentDiffCitations: Array.Empty<DeploymentDiffCitation>(),
            SessionId:               existing.SessionId,
            IsNewSession:            false,
            SessionExpiresAtUtc:     null,
            UsedSessionContext:      false,
            SessionReasonCode:       "DedupReused",
            WasDeduplicated:         true);

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

    private static int ParseRunbookCitationCount(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(summaryJson);
            return doc.RootElement.TryGetProperty("runbookHits", out var prop)
                ? prop.GetInt32()
                : 0;
        }
        catch { return 0; }
    }
}


