namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// INSERT-only audit record. Captures a governance policy decision made during a run.
/// Once created, no fields are updated (the row is immutable).
/// </summary>
public sealed class AgentRunPolicyEvent
{
    // EF Core constructor
    private AgentRunPolicyEvent() { }

    public static AgentRunPolicyEvent Create(
        Guid    runId,
        string  policyName,
        bool    allowed,
        string  reasonCode,
        string  message)
        => new()
        {
            PolicyEventId = Guid.NewGuid(),
            RunId         = runId,
            PolicyName    = policyName,
            Allowed       = allowed,
            ReasonCode    = reasonCode,
            Message       = message,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };

    public Guid           PolicyEventId { get; private set; }
    public Guid           RunId         { get; private set; }
    public string         PolicyName    { get; private set; } = string.Empty;
    public bool           Allowed       { get; private set; }
    public string         ReasonCode    { get; private set; } = string.Empty;
    public string         Message       { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
}
