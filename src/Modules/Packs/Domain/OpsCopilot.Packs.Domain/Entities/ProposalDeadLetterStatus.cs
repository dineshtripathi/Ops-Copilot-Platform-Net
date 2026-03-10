namespace OpsCopilot.Packs.Domain.Entities;

/// <summary>
/// Deterministic status codes for <see cref="ProposalDeadLetterEntry"/>.
/// Values are stored as strings so they are self-describing in the DB.
/// </summary>
public static class ProposalDeadLetterStatus
{
    /// <summary>Entry was dead-lettered and is awaiting its first replay attempt.</summary>
    public const string Pending = "proposal_dead_letter_pending";

    /// <summary>A replay has been dispatched; awaiting outcome.</summary>
    public const string ReplayStarted = "proposal_dead_letter_replay_started";

    /// <summary>Replay succeeded; the SafeAction proposal was recorded.</summary>
    public const string ReplaySucceeded = "proposal_dead_letter_replay_succeeded";

    /// <summary>Most recent replay attempt failed; entry is eligible for retry.</summary>
    public const string ReplayFailed = "proposal_dead_letter_replay_failed";

    /// <summary>All replay attempts exhausted with no success; requires human intervention.</summary>
    public const string ReplayExhausted = "proposal_dead_letter_replay_exhausted";

    /// <summary>An entry with the same AttemptId was already present; this add was a no-op.</summary>
    public const string DuplicateIgnored = "proposal_dead_letter_duplicate_ignored";
}
