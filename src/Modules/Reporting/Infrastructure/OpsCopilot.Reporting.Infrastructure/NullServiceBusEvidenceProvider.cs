using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 91: null-object implementation shipped until a real Service Bus
/// integration is wired. Always returns null — causes section to be absent from UI.
/// </summary>
internal sealed class NullServiceBusEvidenceProvider : IServiceBusEvidenceProvider
{
    public Task<ServiceBusSignals?> GetSignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
        => Task.FromResult<ServiceBusSignals?>(null);
}
