using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Slice 147: Thin MAF IAgent adapter that routes incoming message activities to
/// ITriageOrchestrator. No orchestration logic lives here — 100% delegated.
/// PDD §13: MAF is the orchestration foundation; do not replace with ad-hoc loops.
/// </summary>
public sealed class TriageAgentActivityHandler : ITriageAgentHandler
{
    private readonly ITriageOrchestrator _orchestrator;
    private readonly ILogger<TriageAgentActivityHandler> _log;

    public TriageAgentActivityHandler(
        ITriageOrchestrator orchestrator,
        ILogger<TriageAgentActivityHandler> log)
    {
        _orchestrator = orchestrator;
        _log          = log;
    }

    public async Task OnTurnAsync(
        ITurnContext turnContext,
        CancellationToken cancellationToken = default)
    {
        if (!ActivityTypes.Message.Equals(
                turnContext.Activity.Type,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var activityId = turnContext.Activity.Id ?? Guid.NewGuid().ToString("N");

        _log.LogInformation(
            "MAF: routing message activity {ActivityId} to ITriageOrchestrator",
            activityId);

        var result = await _orchestrator.RunAsync(
            tenantId:         "default",   // TODO Slice 148: extract from JWT claims
            alertFingerprint: activityId,
            workspaceId:      "default",
            ct:               cancellationToken);

        var reply = result.LlmNarrative
                    ?? $"Triage completed with status {result.Status}.";

        await turnContext.SendActivityAsync(reply, cancellationToken: cancellationToken);
    }
}
