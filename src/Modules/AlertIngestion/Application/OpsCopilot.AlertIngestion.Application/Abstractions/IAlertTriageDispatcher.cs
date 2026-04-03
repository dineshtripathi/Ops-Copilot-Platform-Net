namespace OpsCopilot.AlertIngestion.Application.Abstractions;

/// <summary>
/// Port for notifying downstream systems that a new alert has been ingested
/// and a Pending <c>AgentRun</c> is ready for triage.
///
/// The default registration is <see cref="NullAlertTriageDispatcher"/> (no-op).
/// Operators can replace it with a real implementation — e.g. an in-process
/// background channel or a Service Bus enqueuer — without touching alertIngestion
/// application logic.
/// </summary>
public interface IAlertTriageDispatcher
{
    /// <summary>
    /// Notify that a new alert has been ingested and is ready for triage.
    /// </summary>
    /// <param name="tenantId">Tenant that owns the run.</param>
    /// <param name="runId">The <c>AgentRun</c> ID created during ingestion.</param>
    /// <param name="fingerprint">Deterministic fingerprint of the normalised alert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the dispatch was enqueued or triggered;
    /// <c>false</c> when skipped (e.g. no-op implementation not configured).
    /// </returns>
    Task<bool> DispatchAsync(
        string            tenantId,
        Guid              runId,
        string            fingerprint,
        CancellationToken ct = default);
}
