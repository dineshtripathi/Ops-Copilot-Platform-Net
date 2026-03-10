using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Stores proposal recording attempts that have exhausted all retries,
/// preserving them for inspection, alerting, or manual replay.
/// </summary>
public interface IProposalDeadLetterStore
{
    /// <summary>Adds a dead-lettered attempt to the store.</summary>
    Task AddAsync(ProposalRecordingAttempt attempt, CancellationToken ct = default);

    /// <summary>Returns all dead-lettered attempts currently held in the store.</summary>
    Task<IReadOnlyList<ProposalRecordingAttempt>> GetAllAsync(CancellationToken ct = default);
}
