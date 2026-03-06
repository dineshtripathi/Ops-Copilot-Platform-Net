namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Pack evidence execution result surfaced from the Packs module during triage (Mode B+).</summary>
/// <param name="PackName">Name of the pack that owns the evidence collector.</param>
/// <param name="CollectorId">Identifier of the evidence collector within the pack.</param>
/// <param name="ConnectorName">Connector used to execute the query (e.g. "azure-monitor").</param>
/// <param name="QueryFile">Relative path to the KQL query file (may be null).</param>
/// <param name="QueryContent">Full KQL content that was executed (may be null).</param>
/// <param name="ResultJson">Truncated JSON result of the query execution (may be null).</param>
/// <param name="RowCount">Number of rows returned (capped by MaxRows).</param>
/// <param name="ErrorMessage">Error message if execution failed (null on success).</param>
public sealed record PackEvidenceResultDto(
    string  PackName,
    string  CollectorId,
    string  ConnectorName,
    string? QueryFile,
    string? QueryContent,
    string? ResultJson,
    int     RowCount,
    string? ErrorMessage);
