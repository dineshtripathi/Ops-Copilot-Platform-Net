using System.Collections.Concurrent;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Services;

public sealed class EvaluationRunStore
{
    private readonly ConcurrentDictionary<Guid, AsyncEvaluationRun> _runs = new();

    public AsyncEvaluationRun StartRun(Guid runId)
    {
        var run = new AsyncEvaluationRun(runId, EvaluationRunStatus.Running, DateTime.UtcNow);
        _runs[runId] = run;
        return run;
    }

    public void CompleteRun(Guid runId, EvaluationRunSummary summary)
    {
        if (_runs.TryGetValue(runId, out var existing))
            _runs[runId] = existing with { Status = EvaluationRunStatus.Completed, Summary = summary };
    }

    public void FailRun(Guid runId)
    {
        if (_runs.TryGetValue(runId, out var existing))
            _runs[runId] = existing with { Status = EvaluationRunStatus.Failed };
    }

    public AsyncEvaluationRun? GetRun(Guid runId)
    {
        return _runs.TryGetValue(runId, out var run) ? run : null;
    }
}
