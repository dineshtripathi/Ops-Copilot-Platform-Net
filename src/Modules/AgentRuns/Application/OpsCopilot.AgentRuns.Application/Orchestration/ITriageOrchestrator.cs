using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Models;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Slice 129: Dispatcher-facing contract for the triage pipeline.
/// Extracted from the concrete TriageOrchestrator to enable testability
/// of TriageOrchestratorDispatcher without requiring a live DI container.
/// Slice 145: RunAsync added so the stream endpoint can inject the interface.
/// </summary>
public interface ITriageOrchestrator
{
    /// <summary>
    /// Runs the full triage pipeline from scratch for a new alert fingerprint.
    /// </summary>
    Task<TriageResult> RunAsync(
        string tenantId,
        string alertFingerprint,
        string workspaceId,
        int timeRangeMinutes = 120,
        string? alertTitle = null,
        string? subscriptionId = null,
        string? resourceGroup = null,
        Guid? sessionId = null,
        RunContext? context = null,
        AgentRun? existingRun = null,
        CancellationToken ct = default);

    /// <summary>
    /// Applies the full triage pipeline to an existing Pending AgentRun.
    /// </summary>
    Task<TriageResult> ResumeRunAsync(
        AgentRun existingRun,
        string workspaceId,
        int timeRangeMinutes = 120,
        string? alertTitle = null,
        CancellationToken ct = default);
}
