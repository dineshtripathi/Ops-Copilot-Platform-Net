using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application;
using OpsCopilot.BuildingBlocks.Contracts;
using OpsCopilot.BuildingBlocks.Contracts.Tools;

namespace OpsCopilot.AgentRuns.Infrastructure.Tooling;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IKqlQueryTool _kqlQueryTool;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IKqlQueryTool kqlQueryTool, ILogger<ToolExecutor> logger)
    {
        _kqlQueryTool = kqlQueryTool;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ExecuteKqlQueryAsync(AlertPayload alert, CancellationToken cancellationToken)
    {
        var query = $"search * | where ResourceId == '{alert.ResourceId}'";
        var filters = new Dictionary<string, string>
        {
            ["severity"] = alert.Severity,
            ["source"] = alert.Source
        };

        _logger.LogInformation("Executing kql_query for alert {AlertId}", alert.AlertId);
        var result = await _kqlQueryTool.ExecuteAsync(new KqlQueryRequest(query, filters), cancellationToken).ConfigureAwait(false);

        return new ToolExecutionResult("kql_query", result.Query, result.Summary, result.EvidenceId);
    }
}