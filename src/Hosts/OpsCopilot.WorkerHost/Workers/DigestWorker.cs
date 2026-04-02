using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.WorkerHost.Workers;

// ─────────────────────────────────────────────────────────────────────────────
// Digest source abstraction — interface-backed so the real implementation
// (backed by ITenantRegistry + IAgentRunsReportingQueryService) can be wired
// at the composition root without modifying the worker.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-tenant operational digest entry produced by <see cref="ITenantDigestSource"/>.
/// Contains only aggregate metrics — no payload bodies, secrets, or PII.
/// </summary>
/// <param name="TenantId">Tenant identifier.</param>
/// <param name="DisplayName">Human-readable tenant name for log output.</param>
/// <param name="TotalRuns">Total agent runs in the digest window.</param>
/// <param name="FailedRuns">Number of runs with a failure status in the window.</param>
/// <param name="WindowStart">Inclusive start of the digest time window (UTC).</param>
/// <param name="WindowEnd">Exclusive end of the digest time window (UTC).</param>
public sealed record TenantDigestEntry(
    Guid TenantId,
    string DisplayName,
    int TotalRuns,
    int FailedRuns,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd);

/// <summary>
/// Abstraction for collecting per-tenant digest entries over a recent time window.
/// Default registration is <see cref="NullTenantDigestSource"/> (returns an empty list).
/// Replace at the composition root with a real implementation backed by
/// <c>ITenantRegistry</c> and <c>IAgentRunsReportingQueryService</c>.
/// </summary>
public interface ITenantDigestSource
{
    /// <summary>Collect digest entries for all active tenants.</summary>
    Task<IReadOnlyList<TenantDigestEntry>> CollectAsync(CancellationToken ct);
}

/// <summary>
/// No-op source — always returns an empty collection.
/// Registered by default so the worker starts cleanly without a real data store.
/// </summary>
internal sealed class NullTenantDigestSource : ITenantDigestSource
{
    public Task<IReadOnlyList<TenantDigestEntry>> CollectAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TenantDigestEntry>>(Array.Empty<TenantDigestEntry>());
}

// ─────────────────────────────────────────────────────────────────────────────
// Worker
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Background worker that fires on a configurable interval and logs a structured
/// per-tenant operational digest.
///
/// <para>
/// Configuration key: <c>Digest:Worker:IntervalHours</c> (default 24).
/// </para>
/// <para>
/// On each tick the worker asks <see cref="ITenantDigestSource"/> for the latest
/// per-tenant stats and emits one structured log line per tenant plus an aggregate
/// summary. All output is identifier-safe (no payload bodies, secrets, or tokens).
/// </para>
/// </summary>
internal sealed class DigestWorker : BackgroundService
{
    private readonly ITenantDigestSource _source;
    private readonly ILogger<DigestWorker> _logger;
    private readonly TimeSpan _interval;

    public DigestWorker(
        ITenantDigestSource source,
        IConfiguration configuration,
        ILogger<DigestWorker> logger)
    {
        _source = source;
        _logger = logger;

        var hours = configuration.GetValue("Digest:Worker:IntervalHours", 24);
        _interval = TimeSpan.FromHours(Math.Max(hours, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DigestWorker started (interval: {IntervalHours}h)", _interval.TotalHours);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDigestAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in digest worker loop");
            }
        }
    }

    /// <summary>
    /// Collect and log the periodic tenant digest.
    /// Exposed as <c>internal</c> for unit-test access (same pattern as
    /// <see cref="AlertIngestionWorker.ProcessBatchAsync"/> and
    /// <see cref="ProposalDeadLetterReplayWorker.ProcessPendingEntriesAsync"/>).
    /// </summary>
    internal async Task ProcessDigestAsync(CancellationToken ct)
    {
        var entries = await _source.CollectAsync(ct);

        if (entries.Count == 0)
        {
            _logger.LogDebug("DigestWorker: no active tenants, skipping digest");
            return;
        }

        foreach (var entry in entries)
        {
            _logger.LogInformation(
                "DigestWorker: tenant={TenantId} display={DisplayName} " +
                "totalRuns={TotalRuns} failedRuns={FailedRuns} " +
                "window={WindowStart:u}..{WindowEnd:u}",
                entry.TenantId, entry.DisplayName,
                entry.TotalRuns, entry.FailedRuns,
                entry.WindowStart, entry.WindowEnd);
        }

        _logger.LogInformation(
            "DigestWorker: digest complete. Tenants={TenantCount} TotalRuns={TotalRuns} FailedRuns={FailedRuns}",
            entries.Count,
            entries.Sum(e => e.TotalRuns),
            entries.Sum(e => e.FailedRuns));
    }
}
