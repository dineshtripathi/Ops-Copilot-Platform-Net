using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// No-op implementation of <see cref="IObservabilityEvidenceProvider"/> used when the
/// app-insights evidence pack is not configured for the current environment.
/// All methods return null / empty tuples so callers experience a graceful no-data state
/// rather than a dependency-resolution or runtime failure.
/// </summary>
internal sealed class NullObservabilityEvidenceProvider : IObservabilityEvidenceProvider
{
    public Task<ObservabilityEvidenceSummary?> GetSummaryAsync(
        Guid runId, string tenantId, string? workspaceId, CancellationToken ct)
        => Task.FromResult<ObservabilityEvidenceSummary?>(null);

    public Task<ObservabilityEvidenceSummary?> GetLiveSummaryAsync(
        string tenantId, CancellationToken ct)
        => Task.FromResult<ObservabilityEvidenceSummary?>(null);

    public Task<LiveImpactEvidenceSummary?> GetLiveImpactSummaryAsync(
        string tenantId, CancellationToken ct)
        => Task.FromResult<LiveImpactEvidenceSummary?>(null);

    public Task<(ObservabilityEvidenceSummary? Observability, LiveImpactEvidenceSummary? Impact)> GetLiveCombinedAsync(
        string tenantId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
        => Task.FromResult<(ObservabilityEvidenceSummary?, LiveImpactEvidenceSummary?)>((null, null));
}
