namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Determines whether a specific tool may be invoked for a given tenant.
/// Checked BEFORE the MCP call — denied tools never reach MCP.
/// </summary>
public interface IToolAllowlistPolicy
{
    PolicyDecision CanUseTool(string tenantId, string toolName);
}
