namespace OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Lightweight read-only projection of the agentRuns.ToolCalls table.
/// No TenantId column — tenant isolation requires a join to AgentRunReadModel.
/// </summary>
internal sealed class ToolCallReadModel
{
    public Guid ToolCallId { get; init; }
    public Guid RunId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long DurationMs { get; init; }
    public DateTimeOffset ExecutedAtUtc { get; init; }
}
