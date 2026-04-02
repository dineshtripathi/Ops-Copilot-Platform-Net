using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Services;

namespace OpsCopilot.WorkerHost.Workers;

// ─────────────────────────────────────────────────────────────────────────────
// Polling-source abstractions — interface-backed so the queue implementation
// can be swapped (Azure Service Bus, RabbitMQ, in-memory, etc.)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Incoming alert message from a queue source.
/// </summary>
/// <param name="MessageId">Queue-assigned identifier for acknowledgement / dead-lettering.</param>
/// <param name="TenantId">Tenant that owns the alert.</param>
/// <param name="Provider">Provider key (e.g. "azure_monitor", "datadog").</param>
/// <param name="RawJson">Serialised alert payload.</param>
/// <param name="DeliveryCount">How many times this message has been delivered (1-based).</param>
public sealed record IncomingAlertMessage(
    string MessageId,
    string TenantId,
    string Provider,
    string RawJson,
    int DeliveryCount);

/// <summary>
/// Abstraction for polling incoming alert messages.
/// Default registration is <see cref="NullAlertIngestionSource"/> (empty queue).
/// Replace at the composition root with an Azure Service Bus, RabbitMQ, or
/// in-memory channel implementation.
/// </summary>
public interface IAlertIngestionSource
{
    /// <summary>Poll for up to <paramref name="maxMessages"/> pending alerts.</summary>
    Task<IReadOnlyList<IncomingAlertMessage>> PollAsync(int maxMessages, CancellationToken ct);

    /// <summary>Acknowledge successful processing of a message.</summary>
    Task AcknowledgeAsync(string messageId, CancellationToken ct);

    /// <summary>Move a message to the dead-letter sub-queue.</summary>
    Task DeadLetterAsync(string messageId, string reason, CancellationToken ct);
}

/// <summary>
/// No-op source — returns an empty batch on every poll.
/// Registered by default so the worker starts cleanly without a real queue.
/// </summary>
internal sealed class NullAlertIngestionSource : IAlertIngestionSource
{
    public Task<IReadOnlyList<IncomingAlertMessage>> PollAsync(int maxMessages, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<IncomingAlertMessage>>(Array.Empty<IncomingAlertMessage>());

    public Task AcknowledgeAsync(string messageId, CancellationToken ct) => Task.CompletedTask;

    public Task DeadLetterAsync(string messageId, string reason, CancellationToken ct) => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// Worker
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Background worker that polls <see cref="IAlertIngestionSource"/> for incoming
/// alert payloads, normalises them via <see cref="AlertNormalizerRouter"/>, and
/// dispatches through <see cref="IAlertTriageDispatcher"/>.
///
/// <para>
/// Configuration key: <c>AlertIngestion:Worker:PollIntervalSeconds</c> (default 30).
/// </para>
/// <para>
/// Transient failures are retried up to <see cref="MaxRetries"/> deliveries;
/// after that the message is dead-lettered via the source.
/// </para>
/// </summary>
internal sealed class AlertIngestionWorker : BackgroundService
{
    internal const int MaxRetries = 3;
    private const int MaxMessagesPerPoll = 10;

    private readonly IAlertIngestionSource _source;
    private readonly AlertNormalizerRouter _router;
    private readonly IAlertTriageDispatcher _dispatcher;
    private readonly ILogger<AlertIngestionWorker> _logger;
    private readonly TimeSpan _pollInterval;

    public AlertIngestionWorker(
        IAlertIngestionSource source,
        AlertNormalizerRouter router,
        IAlertTriageDispatcher dispatcher,
        IConfiguration configuration,
        ILogger<AlertIngestionWorker> logger)
    {
        _source = source;
        _router = router;
        _dispatcher = dispatcher;
        _logger = logger;

        var seconds = configuration.GetValue("AlertIngestion:Worker:PollIntervalSeconds", 30);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(seconds, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AlertIngestionWorker started (poll interval: {Interval}s)",
            _pollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in alert ingestion worker loop");
            }
        }
    }

    /// <summary>
    /// Process a single batch of messages from the source.
    /// Exposed as <c>internal</c> for unit-test access (same pattern as
    /// <see cref="ProposalDeadLetterReplayWorker.ProcessPendingEntriesAsync"/>).
    /// </summary>
    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        var messages = await _source.PollAsync(MaxMessagesPerPoll, ct);
        if (messages.Count == 0)
            return;

        _logger.LogInformation("Alert ingestion: processing {Count} messages", messages.Count);

        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, ct);
        }
    }

    private async Task ProcessMessageAsync(IncomingAlertMessage message, CancellationToken ct)
    {
        try
        {
            var payload = JsonDocument.Parse(message.RawJson).RootElement;
            var normalized = _router.Normalize(message.Provider, payload);
            var fingerprint = NormalizedAlertFingerprintService.Compute(normalized);

            // Generate a correlation run ID for the dispatch contract. The dispatcher
            // implementation decides whether to create an AgentRun record or look one up.
            var runId = Guid.NewGuid();

            var dispatched = await _dispatcher.DispatchAsync(
                message.TenantId, runId, fingerprint, ct);

            _logger.LogInformation(
                "Alert ingestion: processed message {MessageId} (tenant={TenantId}, dispatched={Dispatched})",
                message.MessageId, message.TenantId, dispatched);

            await _source.AcknowledgeAsync(message.MessageId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Alert ingestion: failed to process message {MessageId} (delivery {Delivery}/{Max})",
                message.MessageId, message.DeliveryCount, MaxRetries);

            if (message.DeliveryCount >= MaxRetries)
            {
                _logger.LogError(
                    "Alert ingestion: dead-lettering message {MessageId} after {Max} attempts",
                    message.MessageId, MaxRetries);
                await _source.DeadLetterAsync(message.MessageId, ex.Message, ct);
            }
            // Otherwise the source will re-deliver on the next poll cycle.
        }
    }
}
