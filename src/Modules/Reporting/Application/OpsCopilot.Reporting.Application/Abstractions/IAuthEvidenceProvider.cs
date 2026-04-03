using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

// Slice 95: classifies identity/auth failure signals from already-persisted run data.
// Reads Status + SummaryJson only — no live auth calls, no secrets in output.
public interface IAuthEvidenceProvider
{
    Task<AuthSignals?> GetSignalsAsync(Guid runId, string tenantId, CancellationToken ct);
}
