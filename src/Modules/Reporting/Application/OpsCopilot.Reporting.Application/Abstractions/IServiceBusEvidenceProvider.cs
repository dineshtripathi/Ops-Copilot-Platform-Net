using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Slice 91: read-only provider for Service Bus triage signals on a run.
/// Returns null if signals are unavailable rather than throwing.
/// Implementations MUST be tenant-isolated (tenantId is mandatory on every call).
/// </summary>
public interface IServiceBusEvidenceProvider
{
    Task<ServiceBusSignals?> GetSignalsAsync(Guid runId, string tenantId, CancellationToken ct);
}
