using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.Packs.Domain.Entities;
using OpsCopilot.Packs.Infrastructure.Persistence;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

public sealed class SqlProposalDeadLetterRepositoryTests
{
    // ── Test infrastructure ──────────────────────────────────────────────────

    private static SqlProposalDeadLetterRepository CreateRepo(string dbName)
    {
        var opts = new DbContextOptionsBuilder<PacksDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestDbContextFactory(opts);
        var logger = NullLogger<SqlProposalDeadLetterRepository>.Instance;
        return new SqlProposalDeadLetterRepository(factory, logger);
    }

    private sealed class TestDbContextFactory(DbContextOptions<PacksDbContext> options)
        : IDbContextFactory<PacksDbContext>
    {
        public PacksDbContext CreateDbContext() => new(options);
    }

    private static ProposalDeadLetterEntry MakeEntry(
        string tenantId = "t1",
        string actionId = "sa-restart",
        int attemptNumber = 1)
        => new(
            id: Guid.NewGuid(),
            attemptId: Guid.NewGuid(),
            tenantId: tenantId,
            triageRunId: Guid.NewGuid(),
            packName: "azure-vm",
            actionId: actionId,
            actionType: "restart_vm",
            parametersJson: null,
            attemptNumber: attemptNumber,
            deadLetteredAt: DateTimeOffset.UtcNow,
            errorMessage: "err");

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_Entry_IsPersisted()
    {
        var repo = CreateRepo(nameof(AddAsync_Entry_IsPersisted));
        var entry = MakeEntry();

        await repo.AddAsync(entry);

        var pending = await repo.GetPendingAsync();
        Assert.Single(pending);
        Assert.Equal(entry.Id, pending[0].Id);
    }

    [Fact]
    public async Task ExistsAsync_KnownAttemptId_ReturnsTrue()
    {
        var repo = CreateRepo(nameof(ExistsAsync_KnownAttemptId_ReturnsTrue));
        var entry = MakeEntry();
        await repo.AddAsync(entry);

        Assert.True(await repo.ExistsAsync(entry.AttemptId));
    }

    [Fact]
    public async Task ExistsAsync_UnknownAttemptId_ReturnsFalse()
    {
        var repo = CreateRepo(nameof(ExistsAsync_UnknownAttemptId_ReturnsFalse));

        Assert.False(await repo.ExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesSucceededAndExhausted()
    {
        var repo = CreateRepo(nameof(GetPendingAsync_ExcludesSucceededAndExhausted));
        var pendingEntry   = MakeEntry(actionId: "sa-pending");
        var succeededEntry = MakeEntry(actionId: "sa-succeeded");
        var exhaustedEntry = MakeEntry(actionId: "sa-exhausted");

        await repo.AddAsync(pendingEntry);
        await repo.AddAsync(succeededEntry);
        await repo.AddAsync(exhaustedEntry);

        await repo.MarkReplayStartedAsync(succeededEntry.Id);
        await repo.MarkReplaySucceededAsync(succeededEntry.Id);
        await repo.MarkReplayStartedAsync(exhaustedEntry.Id);
        await repo.MarkReplayExhaustedAsync(exhaustedEntry.Id, "too many");

        var result = await repo.GetPendingAsync();

        Assert.Single(result);
        Assert.Equal("sa-pending", result[0].ActionId);
    }

    [Fact]
    public async Task MarkReplaySucceededAsync_RemovesEntryFromPending()
    {
        var repo = CreateRepo(nameof(MarkReplaySucceededAsync_RemovesEntryFromPending));
        var entry = MakeEntry();
        await repo.AddAsync(entry);
        await repo.MarkReplayStartedAsync(entry.Id);

        await repo.MarkReplaySucceededAsync(entry.Id);

        Assert.Empty(await repo.GetPendingAsync());
    }

    [Fact]
    public async Task MarkReplayFailedAsync_EntryRemainsInPending()
    {
        var repo = CreateRepo(nameof(MarkReplayFailedAsync_EntryRemainsInPending));
        var entry = MakeEntry();
        await repo.AddAsync(entry);
        await repo.MarkReplayStartedAsync(entry.Id);

        await repo.MarkReplayFailedAsync(entry.Id, "network error");

        var pending = await repo.GetPendingAsync();
        Assert.Single(pending);
        Assert.Equal("network error", pending[0].ReplayError);
    }

    [Fact]
    public async Task MarkReplayExhaustedAsync_RemovesEntryFromPending()
    {
        var repo = CreateRepo(nameof(MarkReplayExhaustedAsync_RemovesEntryFromPending));
        var entry = MakeEntry();
        await repo.AddAsync(entry);
        await repo.MarkReplayStartedAsync(entry.Id);

        await repo.MarkReplayExhaustedAsync(entry.Id, "max retries");

        Assert.Empty(await repo.GetPendingAsync());
    }
}
