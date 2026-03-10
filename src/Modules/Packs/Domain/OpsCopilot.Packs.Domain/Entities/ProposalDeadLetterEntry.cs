namespace OpsCopilot.Packs.Domain.Entities;

/// <summary>
/// Durable EF Core entity representing a SafeAction proposal that exhausted all
/// recording retry attempts and has been dead-lettered for replay.
/// </summary>
public sealed class ProposalDeadLetterEntry
{
    /// <summary>EF Core requires a parameterless constructor or owned-entity convention.</summary>
    private ProposalDeadLetterEntry() { }

    public ProposalDeadLetterEntry(
        Guid id,
        Guid attemptId,
        string tenantId,
        Guid triageRunId,
        string packName,
        string actionId,
        string actionType,
        string? parametersJson,
        int attemptNumber,
        DateTimeOffset deadLetteredAt,
        string errorMessage)
    {
        Id = id;
        AttemptId = attemptId;
        TenantId = tenantId;
        TriageRunId = triageRunId;
        PackName = packName;
        ActionId = actionId;
        ActionType = actionType;
        ParametersJson = parametersJson;
        AttemptNumber = attemptNumber;
        DeadLetteredAt = deadLetteredAt;
        ErrorMessage = errorMessage;
        Status = ProposalDeadLetterStatus.Pending;
        ReplayAttempts = 0;
    }

    public Guid Id { get; private set; }

    /// <summary>Unique idempotency key – maps to ProposalRecordingAttempt.AttemptId.</summary>
    public Guid AttemptId { get; private set; }

    public string TenantId { get; private set; } = string.Empty;
    public Guid TriageRunId { get; private set; }
    public string PackName { get; private set; } = string.Empty;
    public string ActionId { get; private set; } = string.Empty;
    public string ActionType { get; private set; } = string.Empty;
    public string? ParametersJson { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTimeOffset DeadLetteredAt { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;

    public string Status { get; private set; } = ProposalDeadLetterStatus.Pending;
    public int ReplayAttempts { get; private set; }
    public DateTimeOffset? LastReplayedAt { get; private set; }
    public string? ReplayError { get; private set; }

    // --- Status transitions (guard against invalid progression) ---

    public void MarkReplayStarted()
    {
        Status = ProposalDeadLetterStatus.ReplayStarted;
        ReplayAttempts++;
        LastReplayedAt = DateTimeOffset.UtcNow;
        ReplayError = null;
    }

    public void MarkReplaySucceeded()
    {
        Status = ProposalDeadLetterStatus.ReplaySucceeded;
    }

    public void MarkReplayFailed(string error)
    {
        Status = ProposalDeadLetterStatus.ReplayFailed;
        ReplayError = error;
    }

    public void MarkReplayExhausted(string error)
    {
        Status = ProposalDeadLetterStatus.ReplayExhausted;
        ReplayError = error;
    }

    public void MarkDuplicateIgnored()
    {
        Status = ProposalDeadLetterStatus.DuplicateIgnored;
    }
}
