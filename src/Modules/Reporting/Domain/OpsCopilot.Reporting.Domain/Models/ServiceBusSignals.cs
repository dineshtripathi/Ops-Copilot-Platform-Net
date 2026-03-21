namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 91: read-only Service Bus triage signals for a run.
/// Contains queue-level health indicators only — no connection strings, no message payloads.
/// </summary>
public sealed record ServiceBusSignals(
    int TotalQueues,
    int TotalActiveMessages,
    int TotalDeadLetterMessages,
    IReadOnlyList<ServiceBusQueueSignal> Queues);

/// <summary>
/// Health signal for a single Service Bus queue.
/// HealthSignal is one of: "healthy" | "warning" | "critical"
/// </summary>
public sealed record ServiceBusQueueSignal(
    string QueueName,
    int    ActiveCount,
    int    DeadLetterCount,
    string HealthSignal);
