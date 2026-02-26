namespace OpsCopilot.AlertIngestion.Presentation.Contracts;

/// <summary>
/// Request body for POST /ingest/alert.
/// </summary>
/// <param name="Provider">Provider key: azure_monitor, datadog, generic, etc.</param>
/// <param name="Payload">Raw JSON of the incoming alert.</param>
public sealed record IngestAlertRequest(string Provider, string Payload);

/// <summary>Response body for POST /ingest/alert.</summary>
/// <param name="RunId">Identifier of the AgentRun ledger entry created for this alert.</param>
/// <param name="Fingerprint">Deterministic SHA-256 hex fingerprint of normalized alert fields.</param>
public sealed record IngestAlertResponse(Guid RunId, string Fingerprint);

/// <summary>Validation-error response body.</summary>
/// <param name="ReasonCode">Machine-readable frozen reason code.</param>
/// <param name="Message">Human-readable error message.</param>
public sealed record IngestAlertErrorResponse(string ReasonCode, string Message);
