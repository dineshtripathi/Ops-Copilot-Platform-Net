namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Port for the deployment_diff MCP tool.
/// ApiHost/Application MUST NOT reference Azure.ResourceGraph directly.
/// Only McpHost executes the resource graph query.
/// </summary>
public interface IDeploymentDiffToolClient
{
    Task<DeploymentDiffResponse> ExecuteAsync(
        DeploymentDiffRequest request, CancellationToken ct = default);
}
