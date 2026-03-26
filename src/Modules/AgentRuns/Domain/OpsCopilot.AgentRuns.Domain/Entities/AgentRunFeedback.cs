namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// Slice 123: Operator feedback record for a completed agent run.
/// INSERT-only — rows are never updated after creation (supports ledger immutability, PDD Invariant 9).
/// Rating: 1 (poor) – 5 (excellent). Comment is optional free-text.
/// One feedback record per run is the expected usage; the schema allows multiples
/// but the endpoint rejects a second submission for the same runId.
/// </summary>
public sealed class AgentRunFeedback
{
    // EF Core constructor
    private AgentRunFeedback() { }

    /// <summary>Creates a new feedback record. RunId must reference an existing AgentRun.</summary>
    public static AgentRunFeedback Create(
        Guid    runId,
        string  tenantId,
        int     rating,
        string? comment)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5 inclusive.");

        return new AgentRunFeedback
        {
            FeedbackId      = Guid.NewGuid(),
            RunId           = runId,
            TenantId        = tenantId,
            Rating          = rating,
            Comment         = comment,
            SubmittedAtUtc  = DateTimeOffset.UtcNow,
        };
    }

    public Guid            FeedbackId     { get; private set; }
    public Guid            RunId          { get; private set; }
    public string          TenantId       { get; private set; } = string.Empty;

    /// <summary>1 = poor, 5 = excellent.</summary>
    public int             Rating         { get; private set; }

    /// <summary>Optional operator comment. Capped at 2 000 characters in storage.</summary>
    public string?         Comment        { get; private set; }

    public DateTimeOffset  SubmittedAtUtc { get; private set; }
}
