using OpsCopilot.Packs.Domain.Entities;

namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Durable repository for <see cref="ProposalDeadLetterEntry"/> records.
/// Provides the full lifecycle required by the replay background worker.
/// </summary>
public interface IProposalDeadLetterRepository
{
    /// <summary>
    /// Persists a new dead-letter entry.  The caller must verify idempotency via
    /// <see cref="ExistsAsync"/> before calling this method, or use the idempotent
    /// <see cref="IProposalDeadLetterStore.AddAsync"/> which wraps both.
    /// </summary>
    Task AddAsync(ProposalDeadLetterEntry entry, CancellationToken ct = default);

    /// <summary>Returns true if an entry with the given <paramref name="attemptId"/> already exists.</summary>
    Task<bool> ExistsAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>Returns all entries whose status is <see cref="ProposalDeadLetterStatus.Pending"/>
    /// or <see cref="ProposalDeadLetterStatus.ReplayFailed"/>, ordered by DeadLetteredAt ascending.</summary>
    Task<IReadOnlyList<ProposalDeadLetterEntry>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Transitions the entry to <see cref="ProposalDeadLetterStatus.ReplayStarted"/>
    /// and increments ReplayAttempts.</summary>
    Task MarkReplayStartedAsync(Guid id, CancellationToken ct = default);

    /// <summary>Transitions the entry to <see cref="ProposalDeadLetterStatus.ReplaySucceeded"/>.</summary>
    Task MarkReplaySucceededAsync(Guid id, CancellationToken ct = default);

    /// <summary>Transitions the entry to <see cref="ProposalDeadLetterStatus.ReplayFailed"/>
    /// and records the error so it will be retried on the next poll.</summary>
    Task MarkReplayFailedAsync(Guid id, string error, CancellationToken ct = default);

    /// <summary>Transitions the entry to <see cref="ProposalDeadLetterStatus.ReplayExhausted"/>
    /// when no further replay will be attempted.</summary>
    Task MarkReplayExhaustedAsync(Guid id, string error, CancellationToken ct = default);
}
