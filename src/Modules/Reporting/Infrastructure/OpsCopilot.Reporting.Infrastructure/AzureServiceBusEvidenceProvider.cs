using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.ServiceBus;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 92 — reads Azure Service Bus queue runtime properties and maps them to
/// ServiceBusSignals using deterministic health thresholds (no LLM, no mutations,
/// no message payloads or connection strings ever logged).
///
/// Health thresholds:
///   DLQ > 0          → "critical"
///   active &gt; 100  → "warning"
///   otherwise         → "healthy"
///
/// Queues are capped at <see cref="MaxQueues"/> to bound the per-request latency.
/// Exceptions are caught and logged as warnings so callers receive null (graceful degradation).
/// </summary>
internal sealed class AzureServiceBusEvidenceProvider(
    IQueueInfoSource source,
    ILogger<AzureServiceBusEvidenceProvider> logger) : IServiceBusEvidenceProvider
{
    private const int MaxQueues = 50;
    private const int WarningActiveThreshold = 100;

    public async Task<ServiceBusSignals?> GetSignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            var queues      = new List<ServiceBusQueueSignal>();
            int totalActive = 0;
            int totalDlq    = 0;

            await foreach (var q in source.GetQueuesAsync(ct))
            {
                if (queues.Count >= MaxQueues)
                    break;

                var health = q.DeadLetterCount > 0         ? "critical"
                           : q.ActiveCount > WarningActiveThreshold ? "warning"
                           : "healthy";

                queues.Add(new ServiceBusQueueSignal(
                    q.Name,
                    (int)q.ActiveCount,
                    (int)q.DeadLetterCount,
                    health));

                totalActive += (int)q.ActiveCount;
                totalDlq    += (int)q.DeadLetterCount;
            }

            return new ServiceBusSignals(queues.Count, totalActive, totalDlq, queues);
        }
        catch (Exception ex)
        {
            // Log identifier only — no payload, no connection string, no token.
            logger.LogWarning(ex,
                "Service Bus evidence collection failed for run {RunId}", runId);
            return null;
        }
    }
}
