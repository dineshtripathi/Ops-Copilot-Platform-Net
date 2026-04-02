namespace OpsCopilot.BuildingBlocks.Contracts.Evaluation;

/// <summary>
/// Cross-module DTO for recording a single online evaluation observation.
/// Captures retrieval quality and optional user feedback for a triage run.
/// Slice 180 — §6.15 Online Eval bridge.
/// </summary>
public sealed record RunEvalRecord(
    Guid            RunId,
    double          RetrievalConfidence,
    float?          FeedbackScore,
    string          ModelVersion,
    string          PromptVersionId,
    DateTimeOffset  RecordedAt);
