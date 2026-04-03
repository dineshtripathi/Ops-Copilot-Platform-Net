using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Slice 97: derives proposed next actions deterministically from already-resolved evidence DTOs.
/// Implementations must be pure (no I/O, no LLM, no mutations).
/// </summary>
public interface IProposalDraftingService
{
    /// <summary>
    /// Derives a prioritised list of proposed next actions from safe evidence already
    /// gathered for the run.  Returns an empty list when no actionable signals exist.
    /// All inputs are nullable — the method must handle any combination gracefully.
    /// </summary>
    IReadOnlyList<ProposedNextAction> DraftProposals(
        RunBriefing?       briefing,
        IncidentSynthesis? synthesis,
        ConnectivitySignals? connectivity,
        AuthSignals?         auth,
        AzureChangeSynthesis? azureChange,
        ServiceBusSignals?   serviceBus);
}
