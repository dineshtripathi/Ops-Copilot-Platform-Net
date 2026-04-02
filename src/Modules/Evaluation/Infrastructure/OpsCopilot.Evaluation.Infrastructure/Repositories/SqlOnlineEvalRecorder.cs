namespace OpsCopilot.Evaluation.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using OpsCopilot.Evaluation.Application.OnlineEval;
using OpsCopilot.Evaluation.Infrastructure.Persistence;

internal sealed class SqlOnlineEvalRecorder(DbContextOptions<EvaluationDbContext> options)
    : IOnlineEvalRecorder
{
    public async Task RecordAsync(OnlineEvalEntry entry, CancellationToken ct = default)
    {
        await using var db = new EvaluationDbContext(options);
        var row = new OnlineEvalRow
        {
            RunId              = entry.RunId,
            RetrievalConfidence = entry.RetrievalConfidence,
            FeedbackScore      = entry.FeedbackScore,
            ModelVersion       = entry.ModelVersion,
            PromptVersionId    = entry.PromptVersionId,
            RecordedAt         = entry.RecordedAt,
        };
        await db.OnlineEvalEntries.AddAsync(row, ct);
        await db.SaveChangesAsync(ct);
    }
}
