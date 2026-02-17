namespace OpsCopilot.BuildingBlocks.Contracts.Tools;

public interface IKqlQueryTool
{
    Task<KqlQueryResult> ExecuteAsync(KqlQueryRequest request, CancellationToken cancellationToken);
}

public sealed record KqlQueryRequest(string Query, IReadOnlyDictionary<string, string>? Filters);

public sealed record KqlQueryResult(string EvidenceId, string Query, string Summary, IReadOnlyList<string> Rows);