using System.Diagnostics;
using System.Text.Json;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Core Slice 1 orchestrator. Wiring:
///   1. Create AgentRun (Pending)
///   2. Build KQL → call McpHost via IKqlToolClient
///   3. Persist ToolCall (INSERT-only, always)
///   4. Complete AgentRun (Completed | Degraded)
///
/// Hallucination guard: if the tool fails, status = Degraded and the error is
/// recorded in the ToolCall + CitationsJson. No fabricated data ever enters the ledger.
/// </summary>
public sealed class TriageOrchestrator
{
    private readonly IAgentRunRepository _repo;
    private readonly IKqlToolClient      _kql;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public TriageOrchestrator(IAgentRunRepository repo, IKqlToolClient kql)
    {
        _repo = repo;
        _kql  = kql;
    }

    public async Task<TriageResult> RunAsync(
        string tenantId,
        string alertFingerprint,
        string workspaceId,
        int    timeRangeMinutes = 120,
        CancellationToken ct = default)
    {
        var run = await _repo.CreateRunAsync(tenantId, alertFingerprint, ct);

        var kql      = $"union traces, exceptions | where timestamp > ago({timeRangeMinutes}m) | take 20";
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
            // Tool threw — record degraded state with full error context
            response = new KqlToolResponse(
                Ok:            false,
                Rows:          Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ExecutedQuery: kql,
                WorkspaceId:   workspaceId,
                Timespan:      timespan,
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                Error:         ex.Message);
            toolStatus = "Failed";

            var citation     = BuildCitation(response);
            var citationsJson = JsonSerializer.Serialize(new[] { citation }, JsonOpts);

            await _repo.AppendToolCallAsync(
                ToolCall.Create(run.RunId, "kql_query",
                    JsonSerializer.Serialize(request, JsonOpts),
                    JsonSerializer.Serialize(response, JsonOpts),
                    toolStatus, sw.ElapsedMilliseconds, citationsJson), ct);

            await _repo.CompleteRunAsync(run.RunId, AgentRunStatus.Degraded,
                JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts),
                citationsJson, ct);

            return new TriageResult(run.RunId, AgentRunStatus.Degraded, null, new[] { citation });
        }

        sw.Stop();

        var toolCitation     = BuildCitation(response);
        var toolCitationsJson = JsonSerializer.Serialize(new[] { toolCitation }, JsonOpts);

        await _repo.AppendToolCallAsync(
            ToolCall.Create(run.RunId, "kql_query",
                JsonSerializer.Serialize(request, JsonOpts),
                JsonSerializer.Serialize(response, JsonOpts),
                toolStatus, sw.ElapsedMilliseconds, toolCitationsJson), ct);

        var finalStatus  = response.Ok ? AgentRunStatus.Completed : AgentRunStatus.Degraded;
        var summaryJson  = response.Ok
            ? JsonSerializer.Serialize(new { rowCount = response.Rows.Count }, JsonOpts)
            : JsonSerializer.Serialize(new { error = response.Error }, JsonOpts);

        await _repo.CompleteRunAsync(run.RunId, finalStatus, summaryJson, toolCitationsJson, ct);

        return new TriageResult(run.RunId, finalStatus, summaryJson, new[] { toolCitation });
    }

    private static KqlCitation BuildCitation(KqlToolResponse r)
        => new(r.WorkspaceId, r.ExecutedQuery, r.Timespan, r.ExecutedAtUtc);
}

/// <summary>Value returned to the presentation layer after a triage run.</summary>
public sealed record TriageResult(
    Guid                      RunId,
    AgentRunStatus            Status,
    string?                   SummaryJson,
    IReadOnlyList<KqlCitation> Citations);
