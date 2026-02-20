namespace OpsCopilot.AlertIngestion.Application.Commands;

/// <summary>
/// Command to ingest a raw alert payload and create a pending AgentRun ledger entry.
/// </summary>
/// <param name="TenantId">Tenant that owns the alert (from x-tenant-id header).</param>
/// <param name="RawJson">Raw JSON string of the incoming alert payload.</param>
public sealed record IngestAlertCommand(
    string TenantId,
    string RawJson);

/// <summary>Result returned by <see cref="IngestAlertCommandHandler"/>.</summary>
/// <param name="RunId">New AgentRun identifier created for this alert.</param>
/// <param name="Fingerprint">SHA-256 hex fingerprint of the raw payload.</param>
public sealed record IngestAlertResult(
    Guid   RunId,
    string Fingerprint);
