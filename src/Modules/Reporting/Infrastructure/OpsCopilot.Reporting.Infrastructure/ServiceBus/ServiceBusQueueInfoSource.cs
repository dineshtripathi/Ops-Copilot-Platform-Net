using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus.Administration;

namespace OpsCopilot.Reporting.Infrastructure.ServiceBus;

/// <summary>
/// Adapts ServiceBusAdministrationClient.GetQueuesRuntimePropertiesAsync to IQueueInfoSource.
/// Slice 92 — wraps the Azure SDK call; its output (QueueRuntimeProperties) has internal
/// constructors so this thin adapter keeps test seam clean.
/// </summary>
internal sealed class ServiceBusQueueInfoSource(ServiceBusAdministrationClient client) : IQueueInfoSource
{
    public async IAsyncEnumerable<QueueInfo> GetQueuesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var props in client.GetQueuesRuntimePropertiesAsync(ct).WithCancellation(ct))
        {
            yield return new QueueInfo(props.Name, props.ActiveMessageCount, props.DeadLetterMessageCount);
        }
    }
}
