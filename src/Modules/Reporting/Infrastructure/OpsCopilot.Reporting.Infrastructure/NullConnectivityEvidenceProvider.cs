using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 94: null provider — returns null so no signals are shown when unavailable.
/// </summary>
internal sealed class NullConnectivityEvidenceProvider : IConnectivityEvidenceProvider
{
    public Task<ConnectivitySignals?> GetSignalsAsync(Guid runId, string tenantId, CancellationToken ct)
        => Task.FromResult<ConnectivitySignals?>(null);
}
