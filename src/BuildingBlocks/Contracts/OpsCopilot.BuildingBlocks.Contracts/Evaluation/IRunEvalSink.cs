namespace OpsCopilot.BuildingBlocks.Contracts.Evaluation;

/// <summary>
/// Cross-module sink for online evaluation records.
/// Implementations live in the Evaluation module; this interface allows any
/// module that references <c>BuildingBlocks.Contracts</c> to emit eval records
/// without depending on Evaluation module internals.
/// Slice 180 — §6.15 Online Eval bridge.
/// </summary>
public interface IRunEvalSink
{
    Task RecordAsync(RunEvalRecord record, CancellationToken ct = default);
}
