using OpsCopilot.BuildingBlocks.Contracts.Tools;

namespace OpsCopilot.McpHost.Tools;

public sealed class FakeKqlQueryTool : IKqlQueryTool
{
    public Task<KqlQueryResult> ExecuteAsync(KqlQueryRequest request, CancellationToken cancellationToken)
    {
        var evidenceId = $"kql-{Guid.NewGuid():N}";
        var rows = new List<string>
        {
            $"Simulated row for query: {request.Query}"
        };

        var summary = "Simulated KQL response. No live data queried.";
        var result = new KqlQueryResult(evidenceId, request.Query, summary, rows);
        return Task.FromResult(result);
    }
}