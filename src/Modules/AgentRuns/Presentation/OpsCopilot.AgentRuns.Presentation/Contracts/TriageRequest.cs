namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Request body for POST /agent/triage.</summary>
/// <param name="AlertPayload">Raw JSON string of the alert to triage.</param>
/// <param name="TimeRangeMinutes">
///   Look-back window for KQL queries, in minutes. Defaults to 120.
/// </param>
public sealed record TriageRequest(
    string AlertPayload,
    int    TimeRangeMinutes = 120);
