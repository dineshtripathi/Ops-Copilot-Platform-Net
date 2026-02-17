using OpsCopilot.BuildingBlocks.Contracts;

namespace OpsCopilot.AgentRuns.Application;

public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteKqlQueryAsync(AlertPayload alert, CancellationToken cancellationToken);
}

public sealed record ToolExecutionResult(string ToolName, string Input, string Output, string EvidenceId);