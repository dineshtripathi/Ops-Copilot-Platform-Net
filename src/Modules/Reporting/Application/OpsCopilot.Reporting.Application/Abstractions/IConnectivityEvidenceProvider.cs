using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Slice 94: classifies networking/connectivity signals from already-persisted run data.
/// Reads Status + SummaryJson only — no live network calls, no raw error strings in output.
/// </summary>
public interface IConnectivityEvidenceProvider
{
    Task<ConnectivitySignals?> GetSignalsAsync(Guid runId, string tenantId, CancellationToken ct);
}
