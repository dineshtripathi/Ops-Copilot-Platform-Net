using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Entities;

namespace OpsCopilot.WorkerHost.Workers;

/// <summary>
/// Background worker that periodically polls for dead-lettered SafeAction proposals
/// and replays them via <see cref="IPackSafeActionRecorder"/>.
/// Adds no execution capability — recording only.
/// </summary>
internal sealed class ProposalDeadLetterReplayWorker : BackgroundService
{
    private const int MaxReplayAttempts = 3;

    private readonly IProposalDeadLetterRepository _repository;
    private readonly IPackSafeActionRecorder _recorder;
    private readonly ILogger<ProposalDeadLetterReplayWorker> _logger;

    public ProposalDeadLetterReplayWorker(
        IProposalDeadLetterRepository repository,
        IPackSafeActionRecorder recorder,
        ILogger<ProposalDeadLetterReplayWorker> logger)
    {
        _repository = repository;
        _recorder = recorder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingEntriesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in dead-letter replay loop");
            }
        }
    }

    internal async Task ProcessPendingEntriesAsync(CancellationToken ct)
    {
        var pending = await _repository.GetPendingAsync(ct);
        if (pending.Count == 0)
            return;

        _logger.LogInformation("Dead-letter replay: processing {Count} pending entries", pending.Count);

        foreach (var entry in pending)
        {
            await ReplayEntryAsync(entry, ct);
        }
    }

    private async Task ReplayEntryAsync(ProposalDeadLetterEntry entry, CancellationToken ct)
    {
        await _repository.MarkReplayStartedAsync(entry.Id, ct);

        try
        {
            var request = BuildRequest(entry);
            var result = await _recorder.RecordAsync(request, ct);

            if (result.FailedCount == 0)
            {
                _logger.LogInformation(
                    "Dead-letter replay succeeded for entry {Id} (pack={Pack}, action={Action})",
                    entry.Id, entry.PackName, entry.ActionId);
                await _repository.MarkReplaySucceededAsync(entry.Id, ct);
            }
            else
            {
                var error = result.Errors.Count > 0
                    ? string.Join("; ", result.Errors)
                    : $"FailedCount={result.FailedCount}";

                _logger.LogWarning(
                    "Dead-letter replay failed for entry {Id}: {Error} (attempt {Attempt}/{Max})",
                    entry.Id, error, entry.ReplayAttempts + 1, MaxReplayAttempts);

                if (entry.ReplayAttempts + 1 >= MaxReplayAttempts)
                    await _repository.MarkReplayExhaustedAsync(entry.Id, error, ct);
                else
                    await _repository.MarkReplayFailedAsync(entry.Id, error, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Dead-letter replay threw for entry {Id} (attempt {Attempt}/{Max})",
                entry.Id, entry.ReplayAttempts + 1, MaxReplayAttempts);

            if (entry.ReplayAttempts + 1 >= MaxReplayAttempts)
                await _repository.MarkReplayExhaustedAsync(entry.Id, ex.Message, ct);
            else
                await _repository.MarkReplayFailedAsync(entry.Id, ex.Message, ct);
        }
    }

    private static PackSafeActionRecordRequest BuildRequest(ProposalDeadLetterEntry entry)
    {
        // Dead-lettered entries only arise from Mode C deployments.
        var proposal = new PackSafeActionProposalItem(
            PackName: entry.PackName,
            ActionId: entry.ActionId,
            DisplayName: entry.ActionId,        // not stored; ActionId is an acceptable display fallback
            ActionType: entry.ActionType,
            RequiresMode: "C",                  // deterministic: dead-letter only exists in Mode C
            DefinitionFile: null,
            ParametersJson: entry.ParametersJson,
            ErrorMessage: null,
            IsExecutableNow: false,             // replay is record-only, not execution
            ExecutionBlockedReason: null);

        return new PackSafeActionRecordRequest(
            DeploymentMode: "C",
            TenantId: entry.TenantId,
            TriageRunId: entry.TriageRunId,
            Proposals: [proposal]);
    }
}
