using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Entities;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure.Persistence;

public sealed class SqlProposalDeadLetterRepository : IProposalDeadLetterStore, IProposalDeadLetterRepository
{
    private readonly IDbContextFactory<PacksDbContext> _factory;
    private readonly ILogger<SqlProposalDeadLetterRepository> _logger;

    public SqlProposalDeadLetterRepository(
        IDbContextFactory<PacksDbContext> factory,
        ILogger<SqlProposalDeadLetterRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // IProposalDeadLetterStore

    public async Task AddAsync(ProposalRecordingAttempt attempt, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var exists = await ctx.ProposalDeadLetterEntries
            .AnyAsync(e => e.AttemptId == attempt.AttemptId, ct);

        if (exists)
        {
            _logger.LogWarning("Duplicate dead-letter attempt {AttemptId} — ignored.", attempt.AttemptId);
            return;
        }

        var entry = new ProposalDeadLetterEntry(
            Guid.NewGuid(),
            attempt.AttemptId,
            attempt.TenantId,
            attempt.TriageRunId,
            attempt.PackName,
            attempt.ActionId,
            attempt.ActionType,
            attempt.ParametersJson,
            attempt.AttemptNumber,
            attempt.AttemptedAt,
            attempt.ErrorMessage);

        ctx.ProposalDeadLetterEntries.Add(entry);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProposalRecordingAttempt>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var entries = await ctx.ProposalDeadLetterEntries
            .OrderBy(e => e.DeadLetteredAt)
            .ToListAsync(ct);

        return entries.Select(e => new ProposalRecordingAttempt(
            e.AttemptId,
            e.TenantId,
            e.TriageRunId,
            e.PackName,
            e.ActionId,
            e.ActionType,
            e.ParametersJson,
            e.AttemptNumber,
            e.DeadLetteredAt,
            e.ErrorMessage,
            IsDeadLettered: true)).ToList();
    }

    // IProposalDeadLetterRepository

    public async Task AddAsync(ProposalDeadLetterEntry entry, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.ProposalDeadLetterEntries.Add(entry);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid attemptId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.ProposalDeadLetterEntries.AnyAsync(e => e.AttemptId == attemptId, ct);
    }

    public async Task<IReadOnlyList<ProposalDeadLetterEntry>> GetPendingAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.ProposalDeadLetterEntries
            .Where(e => e.Status == ProposalDeadLetterStatus.Pending
                     || e.Status == ProposalDeadLetterStatus.ReplayFailed)
            .OrderBy(e => e.DeadLetteredAt)
            .ToListAsync(ct);
    }

    public async Task MarkReplayStartedAsync(Guid id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var entry = await ctx.ProposalDeadLetterEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"ProposalDeadLetterEntry {id} not found.");
        entry.MarkReplayStarted();
        await ctx.SaveChangesAsync(ct);
    }

    public async Task MarkReplaySucceededAsync(Guid id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var entry = await ctx.ProposalDeadLetterEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"ProposalDeadLetterEntry {id} not found.");
        entry.MarkReplaySucceeded();
        await ctx.SaveChangesAsync(ct);
    }

    public async Task MarkReplayFailedAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var entry = await ctx.ProposalDeadLetterEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"ProposalDeadLetterEntry {id} not found.");
        entry.MarkReplayFailed(error);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task MarkReplayExhaustedAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var entry = await ctx.ProposalDeadLetterEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"ProposalDeadLetterEntry {id} not found.");
        entry.MarkReplayExhausted(error);
        await ctx.SaveChangesAsync(ct);
    }
}
