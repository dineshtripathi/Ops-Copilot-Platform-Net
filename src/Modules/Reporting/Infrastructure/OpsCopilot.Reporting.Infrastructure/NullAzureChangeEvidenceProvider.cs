using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

internal sealed class NullAzureChangeEvidenceProvider : IAzureChangeEvidenceProvider
{
    public Task<AzureChangeSynthesis?> GetSynthesisAsync(Guid runId, string tenantId, CancellationToken ct)
        => Task.FromResult<AzureChangeSynthesis?>(null);
}
