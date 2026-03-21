using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Services;

/// <summary>
/// Slice 97: pure deterministic proposal engine — derives proposed next actions from
/// already-resolved evidence DTOs.  No I/O, no LLM, no mutations, no secrets.
/// Each rule maps one identifiable evidence signal to one explainable proposal.
/// </summary>
public sealed class DeterministicProposalEngine : IProposalDraftingService
{
    public IReadOnlyList<ProposedNextAction> DraftProposals(
        RunBriefing?          briefing,
        IncidentSynthesis?    synthesis,
        ConnectivitySignals?  connectivity,
        AuthSignals?          auth,
        AzureChangeSynthesis? azureChange,
        ServiceBusSignals?    serviceBus)
    {
        var proposals = new List<ProposedNextAction>();

        // Auth signals — one proposal per distinct failure
        if (auth?.Signals is { Count: > 0 } authSignals)
        {
            foreach (var s in authSignals)
            {
                proposals.Add(new ProposedNextAction(
                    Proposal:       $"Investigate {s.Category} authentication failure",
                    Rationale:      $"Auth signal: {s.Summary}",
                    SourceCategory: "Auth"));
            }
        }

        // Service Bus dead-letter queues
        if (serviceBus?.Queues is { Count: > 0 } queues)
        {
            foreach (var q in queues.Where(q => q.DeadLetterCount > 0))
            {
                proposals.Add(new ProposedNextAction(
                    Proposal:       $"Review dead-letter queue '{q.QueueName}' ({q.DeadLetterCount} unprocessed messages)",
                    Rationale:      $"Queue '{q.QueueName}' has {q.DeadLetterCount} dead-letter messages",
                    SourceCategory: "ServiceBus"));
            }
        }

        // Connectivity failures
        if (connectivity?.Signals is { Count: > 0 } connSignals)
        {
            foreach (var s in connSignals)
            {
                proposals.Add(new ProposedNextAction(
                    Proposal:       $"Check network connectivity — {s.Category} failure detected",
                    Rationale:      $"Connectivity signal: {s.Summary}",
                    SourceCategory: "Connectivity"));
            }
        }

        // Azure ARM failed deployments
        if (azureChange?.Deployments is { Count: > 0 } deployments)
        {
            foreach (var d in deployments.Where(d =>
                string.Equals(d.ProvisioningState, "Failed", StringComparison.OrdinalIgnoreCase)))
            {
                proposals.Add(new ProposedNextAction(
                    Proposal:       $"Review failed deployment '{d.DeploymentName}' in '{d.ResourceGroup}'",
                    Rationale:      $"ARM deployment '{d.DeploymentName}' has ProvisioningState=Failed",
                    SourceCategory: "AzureChange"));
            }
        }

        // Briefing: run-level failure signal
        if (briefing?.FailureSignal is { Length: > 0 } failSignal)
        {
            proposals.Add(new ProposedNextAction(
                Proposal:       $"Investigate run failure signal: {failSignal}",
                Rationale:      "Run terminated with a failure signal",
                SourceCategory: "Briefing"));
        }

        // Briefing: low tool success rate
        if (briefing?.ToolSuccessRate is double rate && rate < 0.5)
        {
            proposals.Add(new ProposedNextAction(
                Proposal:       "Audit tool configuration — high tool failure rate detected",
                Rationale:      $"Tool success rate is {rate:P0}",
                SourceCategory: "Briefing"));
        }

        return proposals;
    }
}
