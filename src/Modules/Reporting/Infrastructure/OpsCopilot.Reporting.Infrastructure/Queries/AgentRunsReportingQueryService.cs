using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpsCopilot.Rag.Application.Memory;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

namespace OpsCopilot.Reporting.Infrastructure.Queries;

internal sealed class AgentRunsReportingQueryService : IAgentRunsReportingQueryService
{
    private readonly ReportingReadDbContext          _db;
    private readonly IIncidentMemoryRetrievalService _memory;
    private readonly IServiceBusEvidenceProvider     _sbProvider;
    private readonly IAzureChangeEvidenceProvider    _azureChangeProvider;
    private readonly IConnectivityEvidenceProvider   _connectivityProvider;
    private readonly IAuthEvidenceProvider            _authProvider;
    private readonly IProposalDraftingService         _proposals;
    private readonly IEvidenceQualityEvaluator        _evaluator;
    private readonly IDecisionPackBuilder             _packBuilder;

    public AgentRunsReportingQueryService(
        ReportingReadDbContext          db,
        IIncidentMemoryRetrievalService memory,
        IServiceBusEvidenceProvider     sbProvider,
        IAzureChangeEvidenceProvider    azureChangeProvider,
        IConnectivityEvidenceProvider   connectivityProvider,
        IAuthEvidenceProvider           authProvider,
        IProposalDraftingService        proposals,
        IEvidenceQualityEvaluator       evaluator,
        IDecisionPackBuilder            packBuilder)
    {
        _db                   = db;
        _memory               = memory;
        _sbProvider           = sbProvider;
        _azureChangeProvider  = azureChangeProvider;
        _connectivityProvider = connectivityProvider;
        _authProvider         = authProvider;
        _proposals            = proposals;
        _evaluator            = evaluator;
        _packBuilder          = packBuilder;
    }

    public async Task<AgentRunsSummaryReport> GetSummaryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId);

        // Status counts — server-side group
        var statusCounts = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(string status) =>
            statusCounts.FirstOrDefault(s => s.Status == status)?.Count ?? 0;

        int totalRuns = statusCounts.Sum(s => s.Count);

        // Citation coverage — server-side count
        int withCitations = await query.CountAsync(r => r.CitationsJson != null, ct);
        double citationCoverageRate = totalRuns > 0 ? (double)withCitations / totalRuns : 0.0;

        // Average duration — pull paired timestamps to client (DateTimeOffset subtraction
        // is not guaranteed to translate in all EF Core SQL Server provider versions)
        var completedTimes = await query
            .Where(r => r.CompletedAtUtc.HasValue)
            .Select(r => new { r.CreatedAtUtc, CompletedAtUtc = r.CompletedAtUtc!.Value })
            .ToListAsync(ct);

        double? avgDurationMs = completedTimes.Count > 0
            ? completedTimes.Average(x => (x.CompletedAtUtc - x.CreatedAtUtc).TotalMilliseconds)
            : null;

        // Average tokens — server-side (returns null for empty source)
        double? avgTotalTokens = await query
            .Where(r => r.TotalTokens.HasValue)
            .AverageAsync(r => (double?)r.TotalTokens, ct);

        // Total estimated cost — server-side (returns null for empty source in SQL Server)
        decimal? totalEstimatedCost = await query
            .Where(r => r.EstimatedCost.HasValue)
            .SumAsync(r => r.EstimatedCost, ct);

        return new AgentRunsSummaryReport(
            TotalRuns: totalRuns,
            Completed: Count("Completed"),
            Failed: Count("Failed"),
            Degraded: Count("Degraded"),
            Pending: Count("Pending"),
            Running: Count("Running"),
            AvgDurationMs: avgDurationMs,
            AvgTotalTokens: avgTotalTokens,
            TotalEstimatedCost: totalEstimatedCost,
            CitationCoverageRate: citationCoverageRate,
            FromUtc: fromUtc,
            ToUtc: toUtc);
    }

    public async Task<IReadOnlyList<AgentRunsTrendPoint>> GetTrendAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId);

        // Group by calendar day using Year/Month/Day — reliably translates to SQL Server DATEPART
        var raw = await query
            .GroupBy(r => new
            {
                r.CreatedAtUtc.Year,
                r.CreatedAtUtc.Month,
                r.CreatedAtUtc.Day
            })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                Total = g.Count(),
                Completed = g.Count(r => r.Status == "Completed"),
                Failed = g.Count(r => r.Status == "Failed"),
                Degraded = g.Count(r => r.Status == "Degraded")
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);

        return raw
            .Select(x => new AgentRunsTrendPoint(
                DateUtc: new DateOnly(x.Year, x.Month, x.Day),
                TotalRuns: x.Total,
                CompletedRuns: x.Completed,
                FailedRuns: x.Failed,
                DegradedRuns: x.Degraded))
            .ToList();
    }

    public async Task<IReadOnlyList<ExceptionTrendPoint>> GetExceptionTrendAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Where(r => r.IsExceptionSignal ||
                        (r.AlertSourceType != null && r.AlertSourceType.ToLower().Contains("application")));

        var raw = await query
            .GroupBy(r => new
            {
                r.CreatedAtUtc.Year,
                r.CreatedAtUtc.Month,
                r.CreatedAtUtc.Day
            })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                ExceptionSignals = g.Count(),
                Failed = g.Count(r => r.Status == "Failed"),
                Degraded = g.Count(r => r.Status == "Degraded")
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);

        return raw
            .Select(x => new ExceptionTrendPoint(
                DateUtc: new DateOnly(x.Year, x.Month, x.Day),
                ExceptionSignals: x.ExceptionSignals,
                FailedRuns: x.Failed,
                DegradedRuns: x.Degraded))
            .ToList();
    }

    public async Task<IReadOnlyList<DeploymentCorrelationPoint>> GetDeploymentCorrelationAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var runRows = await ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Select(r => new
            {
                r.CreatedAtUtc,
                r.Status,
                r.SummaryJson
            })
            .ToListAsync(ct);

        var byDay = runRows
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAtUtc.UtcDateTime.Date))
            .OrderBy(d => d.Key)
            .ToList();

        var result = new List<DeploymentCorrelationPoint>(byDay.Count);
        foreach (var dayGroup in byDay)
        {
            var runsWithDeploymentChanges = 0;
            var failedOrDegradedWithChanges = 0;

            foreach (var run in dayGroup)
            {
                var hasDeploymentChanges = false;
                if (!string.IsNullOrWhiteSpace(run.SummaryJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(run.SummaryJson);
                        if (doc.RootElement.TryGetProperty("diffHits", out var diffHits) &&
                            diffHits.ValueKind == JsonValueKind.Number &&
                            diffHits.GetInt32() > 0)
                        {
                            hasDeploymentChanges = true;
                        }
                    }
                    catch
                    {
                        hasDeploymentChanges = false;
                    }
                }

                if (!hasDeploymentChanges)
                    continue;

                runsWithDeploymentChanges++;
                if (run.Status == "Failed" || run.Status == "Degraded")
                    failedOrDegradedWithChanges++;
            }

            var failureRate = runsWithDeploymentChanges > 0
                ? Math.Round((double)failedOrDegradedWithChanges / runsWithDeploymentChanges, 2)
                : 0.0;

            result.Add(new DeploymentCorrelationPoint(
                DateUtc: dayGroup.Key,
                RunsWithDeploymentChanges: runsWithDeploymentChanges,
                FailedOrDegradedWithChanges: failedOrDegradedWithChanges,
                FailureRate: failureRate));
        }

        return result;
    }

    public async Task<IReadOnlyList<HotResourceRow>> GetHotResourcesAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, int maxCount, CancellationToken ct)
    {
        var query = ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Where(r => !string.IsNullOrWhiteSpace(r.AzureResourceId) || !string.IsNullOrWhiteSpace(r.AzureApplication));

        var rows = await query
            .Select(r => new
            {
                ResourceKey = r.AzureResourceId ?? r.AzureApplication ?? "unknown",
                r.AzureResourceGroup,
                r.IsExceptionSignal,
                r.Status
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.ResourceKey, r.AzureResourceGroup })
            .Select(g => new HotResourceRow(
                ResourceKey: g.Key.ResourceKey,
                ResourceGroup: g.Key.AzureResourceGroup,
                TotalRuns: g.Count(),
                ExceptionSignals: g.Count(x => x.IsExceptionSignal),
                FailedRuns: g.Count(x => x.Status == "Failed")))
            .OrderByDescending(r => r.ExceptionSignals)
            .ThenByDescending(r => r.FailedRuns)
            .ThenByDescending(r => r.TotalRuns)
            .Take(Math.Clamp(maxCount, 1, 20))
            .ToList();
    }

    public async Task<BlastRadiusSummary> GetBlastRadiusAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var impacted = await ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Where(r => r.Status == "Failed" || r.Status == "Degraded" || r.IsExceptionSignal)
            .Select(r => new
            {
                r.AzureSubscriptionId,
                r.AzureResourceGroup,
                r.AzureResourceId,
                r.AzureApplication
            })
            .ToListAsync(ct);

        return new BlastRadiusSummary(
            ImpactedSubscriptions: impacted.Select(x => x.AzureSubscriptionId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ImpactedResourceGroups: impacted.Select(x => x.AzureResourceGroup).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ImpactedResources: impacted.Select(x => x.AzureResourceId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ImpactedApplications: impacted.Select(x => x.AzureApplication).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    public async Task<ActivitySignalSummary> GetActivitySignalsAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var runIds = ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Select(r => r.RunId);

        var policyRows = await _db.PolicyEvents
            .Where(p => runIds.Contains(p.RunId))
            .Select(p => new { p.Allowed, p.ReasonCode })
            .ToListAsync(ct);

        var policyDenials = policyRows.Count(p => !p.Allowed);
        var scopeDenials = policyRows.Count(p =>
            !p.Allowed && p.ReasonCode.Contains("scope", StringComparison.OrdinalIgnoreCase));
        var budgetDenials = policyRows.Count(p =>
            !p.Allowed &&
            (p.ReasonCode.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
             p.ReasonCode.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
             p.ReasonCode.Contains("cost", StringComparison.OrdinalIgnoreCase)));
        var degradedModeEvents = policyRows.Count(p =>
            p.ReasonCode.Contains("degraded", StringComparison.OrdinalIgnoreCase));

        return new ActivitySignalSummary(
            TotalPolicyEvents: policyRows.Count,
            PolicyDenials: policyDenials,
            ScopeDenials: scopeDenials,
            BudgetDenials: budgetDenials,
            DegradedModeEvents: degradedModeEvents);
    }

    public async Task<IReadOnlyList<DiagnosisHypothesis>> GetTopDiagnosisAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, int maxCount, CancellationToken ct)
    {
        var rows = await ApplyRunFilters(_db.AgentRunRecords, fromUtc, toUtc, tenantId)
            .Where(r => r.Status == "Failed" || r.Status == "Degraded" || r.IsExceptionSignal)
            .Select(r => new { r.Status, r.SummaryJson, r.IsExceptionSignal })
            .Take(500)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return [];

        var scores = new Dictionary<string, (int Score, int Evidence)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication/Authorization"] = (0, 0),
            ["Network/Connectivity"] = (0, 0),
            ["Deployment Change Regression"] = (0, 0),
            ["Service Capacity/Throttling"] = (0, 0),
            ["Application Exception"] = (0, 0),
        };

        foreach (var row in rows)
        {
            var text = (row.SummaryJson ?? string.Empty).ToLowerInvariant();
            var isFailed = row.Status == "Failed";
            var baseWeight = isFailed ? 3 : 2;

            if (text.Contains("401") || text.Contains("403") || text.Contains("unauthorized") || text.Contains("forbidden") || text.Contains("token"))
                scores["Authentication/Authorization"] = (scores["Authentication/Authorization"].Score + baseWeight, scores["Authentication/Authorization"].Evidence + 1);

            if (text.Contains("timeout") || text.Contains("dns") || text.Contains("refused") || text.Contains("unreachable") || text.Contains("gateway"))
                scores["Network/Connectivity"] = (scores["Network/Connectivity"].Score + baseWeight, scores["Network/Connectivity"].Evidence + 1);

            if (text.Contains("deployment") || text.Contains("provision") || text.Contains("rollout") || text.Contains("release"))
                scores["Deployment Change Regression"] = (scores["Deployment Change Regression"].Score + baseWeight, scores["Deployment Change Regression"].Evidence + 1);

            if (text.Contains("429") || text.Contains("throttl") || text.Contains("quota") || text.Contains("capacity"))
                scores["Service Capacity/Throttling"] = (scores["Service Capacity/Throttling"].Score + baseWeight, scores["Service Capacity/Throttling"].Evidence + 1);

            if (row.IsExceptionSignal || text.Contains("exception") || text.Contains("stack") || text.Contains("nullreference") || text.Contains("argument"))
                scores["Application Exception"] = (scores["Application Exception"].Score + baseWeight, scores["Application Exception"].Evidence + 1);
        }

        var totalScore = Math.Max(1, scores.Sum(x => x.Value.Score));

        return scores
            .Where(x => x.Value.Score > 0)
            .OrderByDescending(x => x.Value.Score)
            .Take(Math.Clamp(maxCount, 1, 5))
            .Select(x => new DiagnosisHypothesis(
                Cause: x.Key,
                Score: x.Value.Score,
                Confidence: Math.Round((double)x.Value.Score / totalScore, 2),
                Evidence: $"Matched {x.Value.Evidence} run summaries"))
            .ToList();
    }

    public async Task<IReadOnlyList<ToolUsageSummaryRow>> GetToolUsageAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        IQueryable<AgentRunReadModel> runQuery = _db.AgentRunRecords;
        if (!string.IsNullOrWhiteSpace(tenantId))
            runQuery = runQuery.Where(r => r.TenantId == tenantId);

        IQueryable<ToolCallReadModel> toolQuery = _db.ToolCallRecords;
        if (fromUtc.HasValue)
            toolQuery = toolQuery.Where(tc =>
                tc.ExecutedAtUtc >= new DateTimeOffset(fromUtc.Value, TimeSpan.Zero));
        if (toUtc.HasValue)
            toolQuery = toolQuery.Where(tc =>
                tc.ExecutedAtUtc <= new DateTimeOffset(toUtc.Value, TimeSpan.Zero));

        // Join ToolCalls ← AgentRuns to enforce tenant isolation
        var joinQuery = from tc in toolQuery
                        join ar in runQuery on tc.RunId equals ar.RunId
                        select new { tc.ToolName, tc.Status, tc.DurationMs };

        var rows = await joinQuery
            .GroupBy(x => x.ToolName)
            .Select(g => new
            {
                ToolName = g.Key,
                Total = g.Count(),
                Successful = g.Count(x => x.Status == "Success"),
                Failed = g.Count(x => x.Status == "Failed"),
                TotalDurationMs = g.Sum(x => x.DurationMs)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);

        return rows
            .Select(x => new ToolUsageSummaryRow(
                ToolName: x.ToolName,
                TotalCalls: x.Total,
                SuccessfulCalls: x.Successful,
                FailedCalls: x.Failed,
                AvgDurationMs: x.Total > 0 ? (double)x.TotalDurationMs / x.Total : 0.0))
            .ToList();
    }

    // ── Shared filter helper ────────────────────────────────────────
    private static IQueryable<AgentRunReadModel> ApplyRunFilters(
        IQueryable<AgentRunReadModel> query,
        DateTime? fromUtc, DateTime? toUtc, string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(r => r.TenantId == tenantId);

        if (fromUtc.HasValue)
            query = query.Where(r =>
                r.CreatedAtUtc >= new DateTimeOffset(fromUtc.Value, TimeSpan.Zero));

        if (toUtc.HasValue)
            query = query.Where(r =>
                r.CreatedAtUtc <= new DateTimeOffset(toUtc.Value, TimeSpan.Zero));

        return query;
    }

    private static readonly Dictionary<string, int> _statusRank = new(StringComparer.Ordinal)
    {
        ["Failed"]    = 0,
        ["Degraded"]  = 1,
        ["Running"]   = 2,
        ["Pending"]   = 3,
        ["Completed"] = 4,
    };

    public async Task<IReadOnlyList<RecentRunSummary>> GetRecentRunsAsync(
        string tenantId, int maxCount, string? status, string? sort, CancellationToken ct)
    {
        var query = _db.AgentRunRecords.Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        // newest/oldest: push ordering into DB query before Take for correct pagination
        IQueryable<AgentRunReadModel> ordered = sort == "oldest"
            ? query.OrderBy(r => r.CreatedAtUtc)
            : query.OrderByDescending(r => r.CreatedAtUtc);

        var rows = await ordered.Take(maxCount).ToListAsync(ct);

        // problem-first: client-side re-rank after DB fetch
        IEnumerable<AgentRunReadModel> sorted = sort == "problem-first"
            ? rows.OrderBy(r => _statusRank.TryGetValue(r.Status ?? string.Empty, out var rank) ? rank : 99)
                  .ThenByDescending(r => r.CreatedAtUtc)
            : rows;

        return sorted
            .Select(r => new RecentRunSummary(
                RunId:            r.RunId,
                SessionId:        r.SessionId,
                Status:           r.Status,
                AlertFingerprint: r.AlertFingerprint,
                CreatedAtUtc:     r.CreatedAtUtc,
                CompletedAtUtc:   r.CompletedAtUtc))
            .ToList();
    }

    // Slice 66/68: single-run detail, tenant-scoped.
    // Returns null for both "not found" and "wrong tenant" — no cross-tenant oracle.
    // Slice 68: adds evidence-summary counts/flags (tool calls, actions, citations presence).
    // Raw CitationsJson and SummaryJson are NEVER returned.
    public async Task<RunDetailResponse?> GetRunDetailAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        var r = await _db.AgentRunRecords
            .Where(r => r.RunId == runId && r.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (r is null) return null;

        // Slice 68: tool-call counts — ToolCallReadModel has no TenantId column,
        // so join to AgentRunRecords for explicit tenant isolation (same pattern as GetToolUsageAsync).
        var toolCallStatuses = await (
            from tc in _db.ToolCallRecords
            join ar in _db.AgentRunRecords on tc.RunId equals ar.RunId
            where ar.RunId == runId && ar.TenantId == tenantId
            select tc.Status
        ).ToListAsync(ct);

        int toolCallCount        = toolCallStatuses.Count;
        int toolCallSuccessCount = toolCallStatuses.Count(s => s == "Success");
        int toolCallFailedCount  = toolCallStatuses.Count(s => s == "Failed");

        // Slice 68: safe-action count — ActionRecords has TenantId, direct WHERE is sufficient.
        int actionCount = await _db.ActionRecords
            .CountAsync(a => a.RunId == runId && a.TenantId == tenantId, ct);

        // Slice 86: briefing only for terminal runs; null → section absent on detail page
        RunBriefing? briefing = null;
        if (r.Status is "Completed" or "Degraded" or "Failed")
            briefing = BuildBriefing(r, toolCallCount, toolCallSuccessCount, toolCallFailedCount, actionCount);

        // Slice 88: deterministic next-step recommendations — only when briefing is present
        var runRecs = briefing is not null
            ? BuildRunRecommendations(briefing, toolCallFailedCount, r.SessionId)
            : null;

        // Slice 89: deterministic correlated incident view — only when briefing is present
        var synthesis = briefing is not null
            ? BuildIncidentSynthesis(briefing, toolCallFailedCount, r.SessionId)
            : null;

        // Slice 97: pre-compute evidence signals so they can be shared with proposal engine
        var sbSignals            = await GetServiceBusSignalsAsync(r.RunId, r.TenantId, ct);
        var azureChangeSignals   = await GetAzureChangeSynthesisAsync(r.RunId, r.TenantId, ct);
        var connectivitySignals  = await GetConnectivitySignalsAsync(r.RunId, r.TenantId, ct);
        var authSignals          = await GetAuthSignalsAsync(r.RunId, r.TenantId, ct);

        var proposedActions = _proposals.DraftProposals(
            briefing, synthesis,
            connectivitySignals, authSignals,
            azureChangeSignals, sbSignals);

        // Slice 90: pre-compute so we can pass to the evidence evaluator
        var priorIncidents = await GetSimilarPriorIncidentsAsync(r.AlertFingerprint, r.TenantId, ct);

        // Slice 98: deterministic evidence-quality assessment — no LLM, pure signal counting
        var quality = _evaluator.Evaluate(
            briefing, synthesis,
            sbSignals, azureChangeSignals,
            connectivitySignals, authSignals,
            priorIncidents, runRecs);

        // Slice 99: deterministic decision pack — no LLM, no I/O, tenant-scoped inputs only
        var decisionPack = _packBuilder.Build(
            briefing, synthesis,
            sbSignals, azureChangeSignals,
            connectivitySignals, authSignals,
            priorIncidents, proposedActions, quality);

        return new RunDetailResponse(
            RunId:                r.RunId,
            SessionId:            r.SessionId,
            Status:               r.Status,
            AlertFingerprint:     r.AlertFingerprint,
            CreatedAtUtc:         r.CreatedAtUtc,
            CompletedAtUtc:       r.CompletedAtUtc,
            TotalTokens:          r.TotalTokens,
            EstimatedCost:        r.EstimatedCost,
            ToolCallCount:        toolCallCount,
            ToolCallSuccessCount: toolCallSuccessCount,
            ToolCallFailedCount:  toolCallFailedCount,
            ActionCount:          actionCount,
            HasCitations:         r.CitationsJson is not null,
            Briefing:             briefing,
            RunRecommendations:   runRecs is { Count: > 0 } ? runRecs : null,
            Synthesis:            synthesis,
            // Slice 90: memory-backed similar prior incidents — graceful null on failure
            SimilarPriorIncidents: priorIncidents,
            // Slice 91: Service Bus triage signals
            ServiceBusSignals:    sbSignals,
            // Slice 93: Azure ARM deployment change signals
            AzureChangeSynthesis: azureChangeSignals,
            // Slice 94: networking/connectivity triage signals
            ConnectivitySignals:  connectivitySignals,
            // Slice 95: identity/auth failure signals
            AuthSignals:          authSignals,
            // Slice 97: deterministic proposals from evidence — null when no proposals derived
            ProposedNextActions:  proposedActions is { Count: > 0 } ? proposedActions : null,
            // Slice 98: deterministic evidence-quality assessment
            EvidenceQuality:      quality,
            // Slice 99: operator decision pack — deterministic, no LLM, no raw payloads
            DecisionPack:         decisionPack);
    }

    // Slice 67: all runs for a session, tenant-scoped.
    // Returns null for both "not found" and "wrong tenant" — no cross-tenant oracle.
    // Slice 69: ordered by CreatedAtUtc ASC (chronological timeline order — oldest run first).
    public async Task<SessionDetailResponse?> GetSessionDetailAsync(
        Guid sessionId, string tenantId, CancellationToken ct)
    {
        var rows = await _db.AgentRunRecords
            .Where(r => r.SessionId == sessionId && r.TenantId == tenantId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        var runs = rows
            .Select(r => new RecentRunSummary(
                RunId:            r.RunId,
                SessionId:        r.SessionId,
                Status:           r.Status,
                AlertFingerprint: r.AlertFingerprint,
                CreatedAtUtc:     r.CreatedAtUtc,
                CompletedAtUtc:   r.CompletedAtUtc))
            .ToList();

        // Slice 88: deterministic next-step recommendations — only when briefing is present
        var sessionBriefing = BuildSessionBriefing(runs);
        var sessionRecs = sessionBriefing is not null
            ? BuildSessionRecommendations(sessionBriefing)
            : null;

        // Slice 89: deterministic correlated incident view — only when briefing is present
        var sessionSynthesis = sessionBriefing is not null
            ? BuildSessionIncidentSynthesis(sessionBriefing)
            : null;

        return new SessionDetailResponse(
            SessionId:              sessionId,
            Runs:                   runs,
            Briefing:               sessionBriefing,
            SessionRecommendations: sessionRecs is { Count: > 0 } ? sessionRecs : null,
            SessionSynthesis:       sessionSynthesis);
    }

    // Slice 87: derive SessionBriefing
    // Returns null when runs is empty (defensive guard, caller already checks Count > 0).
    private static SessionBriefing? BuildSessionBriefing(IReadOnlyList<RecentRunSummary> runs)
    {
        if (runs.Count == 0) return null;

        int runCount = runs.Count;
        bool isIsolated = runCount == 1;

        // StatusPattern — what mix of outcomes does the session contain?
        var distinctStatuses = runs.Select(r => r.Status).Distinct().ToList();
        string statusPattern;
        if (distinctStatuses.Count == 1)
        {
            statusPattern = $"uniform:{distinctStatuses[0]}";
        }
        else if (runs[0].Status != "Failed" && runs[^1].Status == "Failed")
        {
            statusPattern = "degrading";
        }
        else if (runs[0].Status == "Failed" && runs[^1].Status != "Failed")
        {
            statusPattern = "recovering";
        }
        else
        {
            statusPattern = "mixed";
        }

        // FingerprintPattern — are the same alerts recurring?
        var fingerprints = runs
            .Select(r => r.AlertFingerprint)
            .Where(f => f is not null)
            .ToList();

        bool hasNulls = runs.Any(r => r.AlertFingerprint is null);

        string fingerprintPattern;
        string? dominantFingerprint = null;
        if (fingerprints.Count == 0)
        {
            fingerprintPattern = "none";
        }
        else if (hasNulls)
        {
            // Some runs have a fingerprint, some don't — heterogeneous picture
            fingerprintPattern = "mixed";
            dominantFingerprint = fingerprints
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count())
                .First().Key;
        }
        else
        {
            // All runs have a non-null fingerprint
            var groups = fingerprints
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (groups.Count > 1)
            {
                fingerprintPattern  = "mixed";
                dominantFingerprint = groups[0].Key;
            }
            else if (groups[0].Count() > 1)
            {
                fingerprintPattern  = "repeated";
                dominantFingerprint = groups[0].Key;
            }
            else
            {
                fingerprintPattern  = "single";
                dominantFingerprint = groups[0].Key;
            }
        }

        // SequenceConclusion — did the last run end better or worse than the first?
        string? sequenceConclusion = isIsolated ? null
            : StatusSeverityOrdinal(runs[^1].Status) > StatusSeverityOrdinal(runs[0].Status) ? "Worsening"
            : StatusSeverityOrdinal(runs[^1].Status) < StatusSeverityOrdinal(runs[0].Status) ? "Improving"
            : "Stable";

        return new SessionBriefing(
            RunCount:           runCount,
            IsIsolated:         isIsolated,
            StatusPattern:      statusPattern,
            FingerprintPattern: fingerprintPattern,
            DominantFingerprint: dominantFingerprint,
            SequenceConclusion: sequenceConclusion);
    }

    private static int StatusSeverityOrdinal(string status) => status switch
    {
        "Completed" => 0,
        "Degraded"  => 1,
        "Failed"    => 2,
        _           => 1
    };

    // Slice 86: derive RunBriefing from persisted truth only.
    // Counts from SummaryJson are only read when the "error" key is absent.
    // Any malformed JSON leaves counts at zero — no exception propagates.
    private static RunBriefing BuildBriefing(
        AgentRunReadModel r, int tcCount, int tcSuccess, int tcFailed, int actionCount)
    {
        int rowCount = 0, runbookHits = 0, memoryHits = 0, diffHits = 0;
        if (!string.IsNullOrWhiteSpace(r.SummaryJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(r.SummaryJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("error", out _))
                {
                    if (root.TryGetProperty("rowCount",    out var p)) rowCount    = p.GetInt32();
                    if (root.TryGetProperty("runbookHits", out var q)) runbookHits = q.GetInt32();
                    if (root.TryGetProperty("memoryHits",  out var m)) memoryHits  = m.GetInt32();
                    if (root.TryGetProperty("diffHits",    out var d)) diffHits    = d.GetInt32();
                }
            }
            catch { /* malformed JSON → counts remain 0 */ }
        }

        int kqlCitationCount = 0;
        if (!string.IsNullOrWhiteSpace(r.CitationsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(r.CitationsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    kqlCitationCount = doc.RootElement.GetArrayLength();
            }
            catch { /* malformed → 0 */ }
        }

        string statusSeverity = r.Status switch
        {
            "Failed"   => "critical",
            "Degraded" => "warning",
            _          => "ok"
        };

        double? durationSeconds = r.CompletedAtUtc.HasValue
            ? (r.CompletedAtUtc.Value - r.CreatedAtUtc).TotalSeconds
            : null;

        double? toolSuccessRate = tcCount > 0 ? (double)tcSuccess / tcCount : null;

        string? failureSignal = r.Status == "Failed"   ? "RunFailed"
                              : r.Status == "Degraded" ? "RunDegraded"
                              : tcFailed > 0           ? "ToolFailures"
                              : null;

        return new RunBriefing(
            StatusSeverity:         statusSeverity,
            DurationSeconds:        durationSeconds,
            ToolSuccessRate:        toolSuccessRate,
            KqlRowCount:            rowCount,
            RunbookHitCount:        runbookHits,
            MemoryHitCount:         memoryHits,
            DeploymentDiffHitCount: diffHits,
            KqlCitationCount:       kqlCitationCount,
            HasRecommendedActions:  actionCount > 0,
            FailureSignal:          failureSignal);
    }

    // Slice 88: deterministic next-step hints for a single run.
    // Derived only from already-safe RunBriefing fields and counts — no raw data.
    private static IReadOnlyList<RunRecommendation> BuildRunRecommendations(
        RunBriefing b, int toolCallFailedCount, Guid? sessionId)
    {
        var recs = new List<RunRecommendation>();

        if (b.FailureSignal == "RunFailed")
            recs.Add(new("InspectToolCalls",   "Inspect failed tool calls for root cause"));
        if (toolCallFailedCount > 0)
            recs.Add(new("InspectToolFailures","Review tool failure details in tool call log"));
        if (b.KqlRowCount == 0)
            recs.Add(new("CheckKqlData",       "Inspect KQL query — no data rows were returned"));
        if (b.RunbookHitCount == 0 && b.StatusSeverity != "ok")
            recs.Add(new("CheckRunbook",       "Review runbook coverage for this alert type"));
        if (b.DeploymentDiffHitCount > 0)
            recs.Add(new("InspectDeployments", "Inspect deployment changes found during this run"));
        if (sessionId.HasValue)
            recs.Add(new("CompareSessionRuns", "Compare this run with other runs in the same session"));

        return recs;
    }

    // Slice 88: deterministic next-step hints for a session.
    // Derived only from already-safe SessionBriefing fields — no raw data.
    private static IReadOnlyList<RunRecommendation> BuildSessionRecommendations(SessionBriefing b)
    {
        var recs = new List<RunRecommendation>();

        if (b.FingerprintPattern == "repeated")
            recs.Add(new("InspectRepeatedFingerprint", "Inspect recurring alert fingerprint pattern"));
        if (b.StatusPattern == "degrading")
            recs.Add(new("ReviewDegradation",          "Review run sequence - session is degrading"));
        if (b.SequenceConclusion == "Worsening")
            recs.Add(new("CompareRuns",                "Compare latest run with the first run of this session"));

        return recs;
    }

    // Slice 91: Service Bus triage signals — graceful null on any failure.
    // TenantId is mandatory so results are always tenant-isolated.
    private async Task<ServiceBusSignals?> GetServiceBusSignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            return await _sbProvider.GetSignalsAsync(runId, tenantId, ct);
        }
        catch { return null; }
    }

    // Slice 93: Azure ARM deployment change signals — graceful null on any failure.
    // TenantId is used as the subscription scope key — no subscription IDs are logged.
    private async Task<AzureChangeSynthesis?> GetAzureChangeSynthesisAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            return await _azureChangeProvider.GetSynthesisAsync(runId, tenantId, ct);
        }
        catch { return null; }
    }

    // Slice 95: identity/auth failure signals — graceful null on any failure.
    // Classification runs against persisted Status + SummaryJson — no live auth calls, no secrets in output.
    private async Task<AuthSignals?> GetAuthSignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            return await _authProvider.GetSignalsAsync(runId, tenantId, ct);
        }
        catch { return null; }
    }

    // Slice 94: networking/connectivity triage signals — graceful null on any failure.
    // Classification runs against persisted Status + SummaryJson — no live network calls.
    private async Task<ConnectivitySignals?> GetConnectivitySignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            return await _connectivityProvider.GetSignalsAsync(runId, tenantId, ct);
        }
        catch { return null; }
    }

    // Slice 90: vector memory retrieval — graceful null on any failure or empty result.
    // TenantId is mandatory so results are always tenant-isolated.
    private async Task<IReadOnlyList<SimilarPriorIncident>?> GetSimilarPriorIncidentsAsync(
        string? alertFingerprint, string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alertFingerprint)) return null;

        try
        {
            var q    = new IncidentMemoryQuery(alertFingerprint, tenantId, MaxResults: 5, MinScore: 0.72);
            var hits = await _memory.SearchAsync(q, ct);
            if (hits.Count == 0) return null;
            return hits
                .Select(h => new SimilarPriorIncident(
                    PriorRunId:       Guid.TryParse(h.RunId, out var g) ? g : Guid.Empty,
                    AlertFingerprint: h.AlertFingerprint,
                    SummarySnippet:   h.SummarySnippet,
                    Score:            h.Score,
                    OccurredAtUtc:    h.CreatedAtUtc))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    // Slice 89: deterministic correlated incident view for a single run.
    // Derived only from already-safe RunBriefing fields and counts — no raw data.
    private static IncidentSynthesis BuildIncidentSynthesis(
        RunBriefing b, int toolCallFailedCount, Guid? sessionId)
    {
        string overall = b.FailureSignal == "RunFailed" ? "Run failed - tool execution error"
                       : b.StatusSeverity == "critical"  ? "Critical severity alert"
                       : b.StatusSeverity == "warning"   ? "Warning severity alert"
                       :                                   "Run completed successfully";

        string? failureMode = null;
        if (b.FailureSignal == "RunFailed" && toolCallFailedCount > 0)
            failureMode = "Tool call failures caused run failure";
        else if (b.ToolSuccessRate is < 1.0)
            failureMode = "Partial tool call failures detected";

        string? dataSignal        = b.KqlRowCount == 0 ? "No data rows returned from KQL queries" : null;
        string? knowledgeGap      = b.RunbookHitCount == 0 && b.StatusSeverity != "ok"
                                    ? "No runbook coverage found for this alert type" : null;
        string? changeCorrelation = b.DeploymentDiffHitCount > 0
                                    ? "Deployment changes detected during this run" : null;
        string? sessionContext    = sessionId.HasValue ? "Run is part of a multi-run session" : null;

        return new IncidentSynthesis(overall, failureMode, dataSignal,
                                     knowledgeGap, changeCorrelation, sessionContext);
    }

    // Slice 89: deterministic correlated incident view for a session.
    // Derived only from already-safe SessionBriefing fields — no raw data.
    private static IncidentSynthesis BuildSessionIncidentSynthesis(SessionBriefing b)
    {
        string overall = b.StatusPattern == "degrading"      ? "Session is degrading - runs are worsening"
                       : b.StatusPattern == "recovering"     ? "Session is recovering - runs are improving"
                       : b.SequenceConclusion == "Worsening" ? "Session outcome is worsening over time"
                       : b.IsIsolated                        ? "Single isolated run in session"
                       :                                       "Session runs are stable";

        string? failureMode    = b.StatusPattern == "degrading"
                                 ? "Degrading run status pattern detected" : null;
        string? dataSignal     = b.FingerprintPattern == "repeated"
                                 ? "Recurring alert fingerprint pattern detected" : null;
        string? sessionContext = b.DominantFingerprint is not null
                                 ? $"Dominant fingerprint: {b.DominantFingerprint}" : null;

        return new IncidentSynthesis(overall, failureMode, dataSignal,
                                     KnowledgeGap: null, ChangeCorrelation: null, sessionContext);
    }
}
