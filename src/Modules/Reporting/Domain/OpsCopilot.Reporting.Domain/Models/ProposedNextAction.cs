namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 97: a single deterministically-derived proposed next action.
/// Derived only from already-resolved safe evidence DTOs — no raw log payloads, no LLM output.
/// Every proposal is fully explainable via its <see cref="Rationale"/>.
/// </summary>
/// <param name="Proposal">Human-readable action text shown to the operator.</param>
/// <param name="Rationale">Which evidence signal triggered this proposal.</param>
/// <param name="SourceCategory">
/// Classifier for the originating evidence band:
/// Auth | Connectivity | ServiceBus | AzureChange | Briefing
/// </param>
public sealed record ProposedNextAction(
    string Proposal,
    string Rationale,
    string SourceCategory);
