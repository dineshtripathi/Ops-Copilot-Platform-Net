namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>
/// Request body for POST /agent/triage.
/// </summary>
/// <param name="AlertPayload">
/// Structured alert payload. <see cref="AlertPayloadDto.AlertSource"/> and
/// <see cref="AlertPayloadDto.Fingerprint"/> are required.
/// </param>
/// <param name="TimeRangeMinutes">
/// Look-back window for KQL queries, in minutes.
/// Must be between 1 and 1440 (24 hours). Defaults to 120.
/// </param>
/// <param name="WorkspaceId">
/// Optional Log Analytics workspace ID override.
/// When null/empty, the server falls back to the <c>WORKSPACE_ID</c> config
/// or environment variable. Allows callers to target a specific workspace
/// without requiring server-side configuration.
/// </param>
public sealed record TriageRequest(
    AlertPayloadDto AlertPayload,
    int             TimeRangeMinutes = 120,
    string?         WorkspaceId      = null,
    Guid?           SessionId        = null);
