namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Represents a single (failed) attempt to record a safe-action proposal,
/// used by the durable recorder for retry tracking and dead-lettering.
/// </summary>
/// <param name="AttemptId">Unique identifier for this attempt record.</param>
/// <param name="TenantId">Tenant that owns the proposal.</param>
/// <param name="TriageRunId">The triage run that sourced the proposal.</param>
/// <param name="PackName">Pack that owns the action.</param>
/// <param name="ActionId">Action identifier within the pack.</param>
/// <param name="ActionType">The type of action (e.g. run-command).</param>
/// <param name="ParametersJson">Serialised parameters, or <c>null</c> if not present.</param>
/// <param name="AttemptNumber">1-based count of how many times recording was attempted.</param>
/// <param name="AttemptedAt">UTC timestamp when the last attempt occurred.</param>
/// <param name="ErrorMessage">Error detail from the last failed attempt.</param>
/// <param name="IsDeadLettered">
/// <c>true</c> when the attempt has exhausted all retries and been moved
/// to the dead-letter store; <c>false</c> when a future retry is expected.
/// </param>
public sealed record ProposalRecordingAttempt(
    Guid              AttemptId,
    string            TenantId,
    Guid              TriageRunId,
    string            PackName,
    string            ActionId,
    string            ActionType,
    string?           ParametersJson,
    int               AttemptNumber,
    DateTimeOffset    AttemptedAt,
    string            ErrorMessage,
    bool              IsDeadLettered);
