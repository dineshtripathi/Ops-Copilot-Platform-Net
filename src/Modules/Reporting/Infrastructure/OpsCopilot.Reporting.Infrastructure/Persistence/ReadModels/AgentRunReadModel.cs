namespace OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Lightweight read-only projection of the agentRuns.AgentRuns table.
/// Only the columns required for reporting are mapped.
/// </summary>
internal sealed class AgentRunReadModel
{
    public Guid RunId { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public Guid? SessionId { get; init; }
    public string? AlertFingerprint { get; init; }
    public string? AlertProvider { get; init; }
    public string? AlertSourceType { get; init; }
    public bool IsExceptionSignal { get; init; }
    public string? AzureSubscriptionId { get; init; }
    public string? AzureResourceGroup { get; init; }
    public string? AzureResourceId { get; init; }
    public string? AzureApplication { get; init; }
    public string? AzureWorkspaceId { get; init; }
    public string? CitationsJson { get; init; }
    // Slice 86: structural triage counters from TriageOrchestrator
    public string? SummaryJson { get; init; }
    public int? TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
}
