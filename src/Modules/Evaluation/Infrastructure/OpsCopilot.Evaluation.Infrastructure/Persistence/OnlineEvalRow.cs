namespace OpsCopilot.Evaluation.Infrastructure.Persistence;

/// <summary>
/// EF Core entity that persists a single <c>OnlineEvalEntry</c> to the
/// <c>eval.OnlineEvalEntries</c> table. Slice 195.
/// </summary>
internal sealed class OnlineEvalRow
{
    public int            Id                   { get; set; }
    public Guid           RunId                { get; set; }
    public double         RetrievalConfidence  { get; set; }
    public float?         FeedbackScore        { get; set; }
    public string         ModelVersion         { get; set; } = string.Empty;
    public string         PromptVersionId      { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt           { get; set; }
}
