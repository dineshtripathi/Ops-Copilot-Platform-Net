using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

public interface IAzureChangeEvidenceProvider
{
    Task<AzureChangeSynthesis?> GetSynthesisAsync(Guid runId, string tenantId, CancellationToken ct);
}
