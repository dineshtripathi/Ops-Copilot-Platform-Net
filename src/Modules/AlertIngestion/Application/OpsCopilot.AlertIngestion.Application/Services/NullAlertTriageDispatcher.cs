using OpsCopilot.AlertIngestion.Application.Abstractions;

namespace OpsCopilot.AlertIngestion.Application.Services;

/// <summary>
/// No-op implementation of <see cref="IAlertTriageDispatcher"/>.
///
/// Registered by default so that the AlertIngestion module is fully functional
/// without any downstream triage wiring. Replace with a real dispatcher at the
/// composition root when triage automation is required.
/// </summary>
public sealed class NullAlertTriageDispatcher : IAlertTriageDispatcher
{
    public Task<bool> DispatchAsync(
        string            tenantId,
        Guid              runId,
        string            fingerprint,
        CancellationToken ct = default)
        => Task.FromResult(false);
}
