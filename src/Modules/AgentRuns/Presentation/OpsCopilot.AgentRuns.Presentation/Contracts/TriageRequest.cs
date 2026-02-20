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
public sealed record TriageRequest(
    AlertPayloadDto AlertPayload,
    int             TimeRangeMinutes = 120);
