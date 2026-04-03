using System.Collections.Concurrent;

namespace OpsCopilot.Evaluation.Application.OnlineEval;

/// <summary>
/// In-memory recorder that retains up to <see cref="Capacity"/> most-recent entries.
/// Thread-safe; oldest entries are dropped when the capacity is exceeded.
/// Used for drift monitoring within a single process lifetime.
/// Slice 168 — §6.15.
/// </summary>
internal sealed class InMemoryOnlineEvalRecorder : IOnlineEvalRecorder
{
    private readonly ConcurrentQueue<OnlineEvalEntry> _entries = new();
    private readonly int _capacity;

    internal const int DefaultCapacity = 500;

    public InMemoryOnlineEvalRecorder(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _capacity = capacity;
    }

    public Task RecordAsync(OnlineEvalEntry entry, CancellationToken ct = default)
    {
        _entries.Enqueue(entry);

        // Trim oldest entries if over capacity
        while (_entries.Count > _capacity)
            _entries.TryDequeue(out _);

        return Task.CompletedTask;
    }

    /// <summary>Returns a snapshot of all currently-held entries (oldest-first).</summary>
    public IReadOnlyList<OnlineEvalEntry> GetAll()
        => _entries.ToArray();
}
