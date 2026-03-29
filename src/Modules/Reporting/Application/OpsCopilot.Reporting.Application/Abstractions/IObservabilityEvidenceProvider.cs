using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Reads governed App Insights / Azure Monitor evidence for a run and converts it
/// into a safe summary for the operator console.
/// </summary>
public interface IObservabilityEvidenceProvider
{
    Task<ObservabilityEvidenceSummary?> GetSummaryAsync(
        Guid runId,
        string tenantId,
        string? workspaceId,
        CancellationToken ct);

    Task<ObservabilityEvidenceSummary?> GetLiveSummaryAsync(
        string tenantId,
        CancellationToken ct);

    Task<LiveImpactEvidenceSummary?> GetLiveImpactSummaryAsync(
        string tenantId,
        CancellationToken ct);

    /// <summary>
    /// Executes the evidence pack once and returns both the observability and
    /// live-impact summaries, avoiding a redundant second pack execution.
    /// </summary>
    /// <param name="tenantId">Tenant for workspace resolution.</param>
    /// <param name="fromUtc">Optional user-selected start of the query window (UTC).</param>
    /// <param name="toUtc">Optional user-selected end of the query window (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(ObservabilityEvidenceSummary? Observability, LiveImpactEvidenceSummary? Impact)> GetLiveCombinedAsync(
        string tenantId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken ct);
}