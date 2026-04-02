namespace OpsCopilot.Tenancy.Application.Abstractions;

/// <summary>
/// Validates connector health for a tenant during onboarding.
/// §6.19 — Onboarding Orchestration (connector health validation).
/// Returns the names of connectors that failed validation;
/// an empty list means all connectors are healthy.
/// </summary>
public interface IConnectorHealthValidator
{
    Task<IReadOnlyList<string>> ValidateConnectorsAsync(
        Guid tenantId,
        CancellationToken ct = default);
}
