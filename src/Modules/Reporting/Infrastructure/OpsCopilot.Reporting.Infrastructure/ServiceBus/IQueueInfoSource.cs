namespace OpsCopilot.Reporting.Infrastructure.ServiceBus;

/// <summary>
/// Abstracts queue-level runtime properties so AzureServiceBusEvidenceProvider
/// can be unit-tested without instantiating SDK-internal QueueRuntimeProperties.
/// Slice 92 — internal to Reporting.Infrastructure; visible to test project via InternalsVisibleTo.
/// </summary>
internal interface IQueueInfoSource
{
    IAsyncEnumerable<QueueInfo> GetQueuesAsync(CancellationToken ct);
}

/// <summary>Minimal queue runtime snapshot — name + active + dead-letter counts.</summary>
internal sealed record QueueInfo(string Name, long ActiveCount, long DeadLetterCount);
