using OpsCopilot.BuildingBlocks.Contracts.Evaluation;
using OpsCopilot.Evaluation.Application.OnlineEval;

namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// Public bridge adapter: maps a cross-module <see cref="RunEvalRecord"/> to
/// the internal <see cref="OnlineEvalEntry"/> and delegates to
/// <see cref="IOnlineEvalRecorder"/>.
/// This is intentionally <c>public</c> so the DI root can register it;
/// the internal recorder types remain invisible to other modules.
/// Slice 180 — §6.15 Online Eval bridge.
/// </summary>
public sealed class OnlineEvalRunEvalSink : IRunEvalSink
{
    private readonly IOnlineEvalRecorder _recorder;

    public OnlineEvalRunEvalSink(IOnlineEvalRecorder recorder) => _recorder = recorder;

    public Task RecordAsync(RunEvalRecord record, CancellationToken ct = default)
    {
        var entry = new OnlineEvalEntry(
            RunId:               record.RunId,
            RetrievalConfidence: record.RetrievalConfidence,
            FeedbackScore:       record.FeedbackScore,
            ModelVersion:        record.ModelVersion,
            PromptVersionId:     record.PromptVersionId,
            RecordedAt:          record.RecordedAt);

        return _recorder.RecordAsync(entry, ct);
    }
}
