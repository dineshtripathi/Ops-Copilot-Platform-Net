using OpsCopilot.BuildingBlocks.Contracts.Evaluation;
using OpsCopilot.Evaluation.Application.OnlineEval;
using Xunit;

namespace OpsCopilot.Modules.Evaluation.Tests;

/// <summary>
/// Slice 180 — §6.15 Online Eval bridge: IRunEvalSink / OnlineEvalRunEvalSink.
/// </summary>
public sealed class OnlineEvalRunEvalSinkTests
{
    private static RunEvalRecord MakeRecord(float? feedback = 0.9f)
        => new(
            RunId:               Guid.NewGuid(),
            RetrievalConfidence: 0.85,
            FeedbackScore:       feedback,
            ModelVersion:        "gpt-4o",
            PromptVersionId:     "v1",
            RecordedAt:          DateTimeOffset.UtcNow);

    [Fact]
    public async Task RecordAsync_DelegatesToRecorder()
    {
        var recorder = new InMemoryOnlineEvalRecorder();
        IRunEvalSink  sink    = new OnlineEvalRunEvalSink(recorder);
        var           record  = MakeRecord();

        await sink.RecordAsync(record);

        var entries = recorder.GetAll();
        Assert.Single(entries);
        Assert.Equal(record.RunId,               entries[0].RunId);
        Assert.Equal(record.RetrievalConfidence, entries[0].RetrievalConfidence);
        Assert.Equal(record.FeedbackScore,       entries[0].FeedbackScore);
        Assert.Equal(record.ModelVersion,        entries[0].ModelVersion);
        Assert.Equal(record.PromptVersionId,     entries[0].PromptVersionId);
        Assert.Equal(record.RecordedAt,          entries[0].RecordedAt);
    }

    [Fact]
    public async Task RecordAsync_NullFeedbackScore_PropagatesNull()
    {
        var recorder = new InMemoryOnlineEvalRecorder();
        IRunEvalSink  sink   = new OnlineEvalRunEvalSink(recorder);
        var           record = MakeRecord(feedback: null);

        await sink.RecordAsync(record);

        Assert.Null(recorder.GetAll()[0].FeedbackScore);
    }

    [Fact]
    public async Task RecordAsync_MultipleRecords_AllStored()
    {
        var recorder = new InMemoryOnlineEvalRecorder();
        IRunEvalSink sink = new OnlineEvalRunEvalSink(recorder);

        await sink.RecordAsync(MakeRecord());
        await sink.RecordAsync(MakeRecord());
        await sink.RecordAsync(MakeRecord());

        Assert.Equal(3, recorder.GetAll().Count);
    }

    [Fact]
    public async Task NullRecorder_RecordAsync_DoesNotThrow()
    {
        var          recorder = new NullOnlineEvalRecorder();
        IRunEvalSink sink     = new OnlineEvalRunEvalSink(recorder);

        var ex = await Record.ExceptionAsync(() => sink.RecordAsync(MakeRecord()));

        Assert.Null(ex);
    }

    [Fact]
    public void OnlineEvalRunEvalSink_ImplementsIRunEvalSink()
    {
        var recorder = new NullOnlineEvalRecorder();
        var sink     = new OnlineEvalRunEvalSink(recorder);

        Assert.IsAssignableFrom<IRunEvalSink>(sink);
    }
}
