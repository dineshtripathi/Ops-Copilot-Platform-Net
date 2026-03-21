using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

// Slice 95: null provider — returns null so no signals shown when unavailable.
internal sealed class NullAuthEvidenceProvider : IAuthEvidenceProvider
{
    public Task<AuthSignals?> GetSignalsAsync(Guid runId, string tenantId, CancellationToken ct)
        => Task.FromResult<AuthSignals?>(null);
}
