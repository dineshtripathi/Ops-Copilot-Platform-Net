using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Repositories;

namespace OpsCopilot.ApiHost.Dispatch;

/// <summary>
/// Slice 130: Periodic background service that finds <see cref="AgentRunStatus.Running"/>
/// runs whose <c>CreatedAtUtc</c> exceeds the configured threshold and drives them to
/// <see cref="AgentRunStatus.Failed"/>. Closes the gap where a server restart prevents the
/// in-process catch block in <see cref="TriageOrchestratorDispatcher"/> from firing.
/// </summary>
internal sealed class StuckRunWatchdog : BackgroundService
{
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly IOptions<StuckRunWatchdogOptions> _options;
    private readonly ILogger<StuckRunWatchdog>      _log;

    public StuckRunWatchdog(
        IServiceScopeFactory              scopeFactory,
        IOptions<StuckRunWatchdogOptions> options,
        ILogger<StuckRunWatchdog>         log)
    {
        _scopeFactory = scopeFactory;
        _options      = options;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        _log.LogInformation(
            "StuckRunWatchdog started. IntervalSeconds={Interval}, ThresholdMinutes={Threshold}",
            opts.IntervalSeconds, opts.ThresholdMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(opts.IntervalSeconds), stoppingToken)
                      .ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested) break;

            await ScanAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task ScanAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-opts.ThresholdMinutes);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();

        IReadOnlyList<AgentRuns.Domain.Entities.AgentRun> stuck;
        try
        {
            stuck = await repo.GetStuckRunsAsync(cutoff, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "StuckRunWatchdog: failed to query stuck runs");
            return;
        }

        if (stuck.Count == 0) return;

        _log.LogWarning("StuckRunWatchdog: found {Count} stuck run(s) older than {Cutoff:O}",
            stuck.Count, cutoff);

        foreach (var run in stuck)
        {
            try
            {
                var errorJson = JsonSerializer.Serialize(
                    new { error = "StuckRunWatchdog", message = "Run exceeded watchdog threshold and was never completed." });

                await repo.CompleteRunAsync(
                    run.RunId,
                    AgentRunStatus.Failed,
                    errorJson,
                    "[]",
                    ct);

                _log.LogWarning(
                    "StuckRunWatchdog: marked run {RunId} (tenant {TenantId}) as Failed",
                    run.RunId, run.TenantId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "StuckRunWatchdog: failed to mark run {RunId} as Failed",
                    run.RunId);
            }
        }
    }
}
