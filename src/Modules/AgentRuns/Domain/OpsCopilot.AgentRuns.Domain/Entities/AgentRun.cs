using OpsCopilot.AgentRuns.Domain.Enums;

namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// Aggregate root for a single agent triage run.
/// Append-only rule: Status, CompletedAtUtc, SummaryJson, CitationsJson are the
/// only mutable fields. All other fields are set at creation and never changed.
/// </summary>
public sealed class AgentRun
{
    // EF Core constructor
    private AgentRun() { }

    public static AgentRun Create(string tenantId, string alertFingerprint)
        => new()
        {
            RunId            = Guid.NewGuid(),
            TenantId         = tenantId,
            AlertFingerprint = alertFingerprint,
            Status           = AgentRunStatus.Pending,
            CreatedAtUtc     = DateTimeOffset.UtcNow,
        };

    public Guid            RunId            { get; private set; }
    public string          TenantId         { get; private set; } = string.Empty;
    public AgentRunStatus  Status           { get; private set; }
    public DateTimeOffset  CreatedAtUtc     { get; private set; }
    public DateTimeOffset? CompletedAtUtc   { get; private set; }
    public string?         AlertFingerprint { get; private set; }
    public string?         SummaryJson      { get; private set; }
    public string?         CitationsJson    { get; private set; }

    /// <summary>
    /// Transitions the run to a terminal state. May only be called once.
    /// </summary>
    public void Complete(AgentRunStatus finalStatus, string summaryJson, string citationsJson)
    {
        if (finalStatus is not (AgentRunStatus.Completed or AgentRunStatus.Degraded or AgentRunStatus.Failed))
            throw new ArgumentException(
                "finalStatus must be Completed, Degraded, or Failed.", nameof(finalStatus));

        Status         = finalStatus;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        SummaryJson    = summaryJson;
        CitationsJson  = citationsJson;
    }
}
