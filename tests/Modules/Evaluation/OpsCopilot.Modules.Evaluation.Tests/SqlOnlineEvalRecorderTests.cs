using Microsoft.EntityFrameworkCore;
using OpsCopilot.Evaluation.Application.OnlineEval;
using OpsCopilot.Evaluation.Infrastructure.Persistence;
using OpsCopilot.Evaluation.Infrastructure.Repositories;
using Xunit;

namespace OpsCopilot.Modules.Evaluation.Tests;

public sealed class SqlOnlineEvalRecorderTests
{
    private static DbContextOptions<EvaluationDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<EvaluationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static OnlineEvalEntry MakeEntry(float? feedback = 0.8f) =>
        new(
            RunId:               Guid.NewGuid(),
            RetrievalConfidence: 0.92,
            FeedbackScore:       feedback,
            ModelVersion:        "gpt-4o-2024-11",
            PromptVersionId:     "v17",
            RecordedAt:          DateTimeOffset.UtcNow
        );

    [Fact]
    public async Task RecordAsync_PersistsAllFields()
    {
        var opts   = BuildOptions();
        var entry  = MakeEntry(0.9f);
        var sut    = new SqlOnlineEvalRecorder(opts);

        await sut.RecordAsync(entry);

        await using var db  = new EvaluationDbContext(opts);
        var row = await db.OnlineEvalEntries.SingleAsync();

        Assert.Equal(entry.RunId,               row.RunId);
        Assert.Equal(entry.RetrievalConfidence, row.RetrievalConfidence);
        Assert.Equal(entry.FeedbackScore,       row.FeedbackScore);
        Assert.Equal(entry.ModelVersion,        row.ModelVersion);
        Assert.Equal(entry.PromptVersionId,     row.PromptVersionId);
        Assert.Equal(entry.RecordedAt,          row.RecordedAt);
    }

    [Fact]
    public async Task RecordAsync_MultipleCalls_AppendsRows()
    {
        var opts = BuildOptions();
        var sut  = new SqlOnlineEvalRecorder(opts);

        await sut.RecordAsync(MakeEntry());
        await sut.RecordAsync(MakeEntry());
        await sut.RecordAsync(MakeEntry());

        await using var db = new EvaluationDbContext(opts);
        Assert.Equal(3, await db.OnlineEvalEntries.CountAsync());
    }

    [Fact]
    public async Task RecordAsync_NullFeedbackScore_StoresNull()
    {
        var opts  = BuildOptions();
        var entry = MakeEntry(feedback: null);
        var sut   = new SqlOnlineEvalRecorder(opts);

        await sut.RecordAsync(entry);

        await using var db  = new EvaluationDbContext(opts);
        var row = await db.OnlineEvalEntries.SingleAsync();
        Assert.Null(row.FeedbackScore);
    }

    [Fact]
    public async Task RecordAsync_AssignedAutoIncrementId()
    {
        var opts = BuildOptions();
        var sut  = new SqlOnlineEvalRecorder(opts);

        await sut.RecordAsync(MakeEntry());

        await using var db = new EvaluationDbContext(opts);
        var row = await db.OnlineEvalEntries.SingleAsync();
        Assert.True(row.Id > 0, "Id must be positive after save.");
    }

    [Fact]
    public async Task RecordAsync_CancellationToken_Respected()
    {
        var opts = BuildOptions();
        var sut  = new SqlOnlineEvalRecorder(opts);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RecordAsync(MakeEntry(), cts.Token));
    }
}
