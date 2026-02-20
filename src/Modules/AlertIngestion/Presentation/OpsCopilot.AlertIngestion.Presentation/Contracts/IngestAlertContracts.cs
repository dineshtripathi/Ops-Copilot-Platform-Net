namespace OpsCopilot.AlertIngestion.Presentation.Contracts;

/// <summary>
/// Request body for POST /ingest/alert.
/// The alert payload is transmitted as a raw JSON string so callers can
/// forward any schema-invariant alert format without prior parsing.
/// </summary>
/// <param name="Payload">Raw JSON of the incoming alert.</param>
public sealed record IngestAlertRequest(string Payload);

/// <summary>Response body for POST /ingest/alert.</summary>
/// <param name="RunId">Identifier of the AgentRun ledger entry created for this alert.</param>
/// <param name="Fingerprint">Deterministic SHA-256 hex fingerprint of the payload.</param>
public sealed record IngestAlertResponse(Guid RunId, string Fingerprint);
