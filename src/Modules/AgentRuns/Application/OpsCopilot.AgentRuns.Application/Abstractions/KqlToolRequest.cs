namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Stable request schema for the MCP kql_query tool.
/// ApiHost sends this; McpHost executes it against Log Analytics.
/// </summary>
/// <param name="TenantId">Tenant that triggered the triage.</param>
/// <param name="WorkspaceIdOrName">LAW workspace GUID (preferred) or resource name.</param>
/// <param name="Kql">KQL query string.</param>
/// <param name="TimespanIso8601">ISO 8601 duration, e.g. "PT120M".</param>
public sealed record KqlToolRequest(
    string TenantId,
    string WorkspaceIdOrName,
    string Kql,
    string TimespanIso8601);
