using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AlertIngestion.Application.Abstractions;

namespace OpsCopilot.ApiHost.Dispatch;

/// <summary>
/// Slice 127: Real <see cref="IAlertTriageDispatcher"/> that bridges alert ingestion to
/// <see cref="TriageOrchestrator"/>. Runs triage in a fire-and-forget task so ingestion
/// HTTP responses are not blocked by the triage pipeline duration.
/// Uses <see cref="IServiceScopeFactory"/> because both <see cref="IAgentRunRepository"/>
/// and <see cref="ITriageOrchestrator"/> are Scoped and cannot be captured by a Singleton.
/// </summary>
internal sealed class TriageOrchestratorDispatcher : IAlertTriageDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TriageOrchestratorDispatcher> _log;

    /// <summary>
    /// Slice 129: Exposes the most-recently-started fire-and-forget triage task so that
    /// unit tests can <c>await</c> it without introducing timing-based delays.
    /// Not used by production code paths.
    /// </summary>
    internal Task? LastTriageTask { get; private set; }

    public TriageOrchestratorDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<TriageOrchestratorDispatcher> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    /// <inheritdoc />
    public async Task<bool> DispatchAsync(
        string tenantId,
        Guid   runId,
        string fingerprint,
        CancellationToken ct = default)
    {
        // Look up the run within a short-lived scope before firing.
        AgentRun? run;
        using (var lookupScope = _scopeFactory.CreateScope())
        {
            var repo = lookupScope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
            run = await repo.GetByRunIdAsync(runId, tenantId, ct);
        }

        if (run is null)
        {
            _log.LogWarning(
                "Dispatch: AgentRun {RunId} not found for tenant {TenantId}; skipping triage",
                runId, tenantId);
            return false;
        }

        var workspaceId = run.AzureWorkspaceId ?? string.Empty;

        // Fire-and-forget — use CancellationToken.None so host shutdown does not
        // abort an in-flight triage run mid-way. Create a fresh scope so Scoped
        // services (ITriageOrchestrator, EF DbContext) outlive the originating request.
        LastTriageTask = Task.Run(async () =>
        {
            using var triageScope = _scopeFactory.CreateScope();
            var orchestrator = triageScope.ServiceProvider.GetRequiredService<ITriageOrchestrator>();
            try
            {
                await orchestrator.ResumeRunAsync(run, workspaceId, ct: CancellationToken.None);
                _log.LogInformation(
                    "Dispatch: triage completed for run {RunId} (tenant {TenantId})",
                    runId, tenantId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Dispatch: triage pipeline threw for run {RunId} (tenant {TenantId})",
                    runId, tenantId);

                // Slice 129: Drive the run to a terminal Failed state so it never stays
                // stuck in Running after an unhandled dispatch exception.
                try
                {
                    var repo = triageScope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
                    var errorJson = JsonSerializer.Serialize(
                        new { error = "UnhandledDispatchException", message = ex.Message });
                    await repo.CompleteRunAsync(
                        runId,
                        AgentRunStatus.Failed,
                        errorJson,
                        "[]",
                        CancellationToken.None);
                }
                catch (Exception failEx)
                {
                    _log.LogError(failEx,
                        "Dispatch: failed to mark run {RunId} as Failed after exception",
                        runId);
                }
            }
        });

        return true;
    }
}
