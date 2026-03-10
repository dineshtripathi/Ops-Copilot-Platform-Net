using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;

namespace OpsCopilot.Packs.Infrastructure;

internal sealed class DurablePackSafeActionRecorder : IPackSafeActionRecorder
{
    private readonly IPackSafeActionRecorder _inner;
    private readonly IProposalRecordingRetryPolicy _retryPolicy;
    private readonly IProposalDeadLetterStore _deadLetterStore;
    private readonly ILogger<DurablePackSafeActionRecorder> _logger;

    public DurablePackSafeActionRecorder(
        IPackSafeActionRecorder inner,
        IProposalRecordingRetryPolicy retryPolicy,
        IProposalDeadLetterStore deadLetterStore,
        ILogger<DurablePackSafeActionRecorder> logger)
    {
        _inner = inner;
        _retryPolicy = retryPolicy;
        _deadLetterStore = deadLetterStore;
        _logger = logger;
    }

    public async Task<PackSafeActionRecordResult> RecordAsync(
        PackSafeActionRecordRequest request,
        CancellationToken ct = default)
    {
        var result = await _inner.RecordAsync(request, ct);

        if (result.FailedCount == 0)
            return result;

        // Partition: accumulate non-failed items; track failed for retry
        var successfulRecords = result.Records
            .Where(r => r.Status != "Failed")
            .ToList();
        var failedItems = result.Records
            .Where(r => r.Status == "Failed")
            .ToList();

        var attempt = 2;
        while (failedItems.Count > 0 && _retryPolicy.ShouldRetry(attempt))
        {
            var delay = _retryPolicy.GetDelay(attempt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);

            var failedActionIds = failedItems.Select(f => f.ActionId).ToHashSet(StringComparer.Ordinal);
            var subRequest = request with
            {
                Proposals = request.Proposals
                    .Where(p => failedActionIds.Contains(p.ActionId))
                    .ToList()
            };

            var retryResult = await _inner.RecordAsync(subRequest, ct);

            successfulRecords.AddRange(retryResult.Records.Where(r => r.Status != "Failed"));
            failedItems = retryResult.Records.Where(r => r.Status == "Failed").ToList();
            attempt++;
        }

        // Dead-letter any items that exhausted all retries
        foreach (var item in failedItems)
        {
            var proposal = request.Proposals.FirstOrDefault(p => p.ActionId == item.ActionId);
            var deadLetter = new ProposalRecordingAttempt(
                AttemptId:      Guid.NewGuid(),
                TenantId:       request.TenantId,
                TriageRunId:    request.TriageRunId,
                PackName:       item.PackName,
                ActionId:       item.ActionId,
                ActionType:     item.ActionType,
                ParametersJson: proposal?.ParametersJson,
                AttemptNumber:  attempt - 1,
                AttemptedAt:    DateTimeOffset.UtcNow,
                ErrorMessage:   item.ErrorMessage ?? string.Empty,
                IsDeadLettered: true);

            await _deadLetterStore.AddAsync(deadLetter, ct);

            _logger.LogWarning(
                "Proposal recording dead-lettered after {Attempts} attempt(s). " +
                "TenantId={TenantId} TriageRunId={TriageRunId} PackName={PackName} ActionId={ActionId} Error={Error}",
                attempt - 1,
                request.TenantId,
                request.TriageRunId,
                item.PackName,
                item.ActionId,
                item.ErrorMessage);
        }

        var allRecords = successfulRecords.Concat(failedItems).ToList();
        var errors = allRecords
            .Where(r => r.ErrorMessage is not null)
            .Select(r => r.ErrorMessage!)
            .ToList();

        return new PackSafeActionRecordResult(
            Records:      allRecords,
            CreatedCount: allRecords.Count(r => r.Status == "Created"),
            SkippedCount: allRecords.Count(r => r.Status == "Skipped"),
            FailedCount:  failedItems.Count,
            Errors:       errors);
    }
}
