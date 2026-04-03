namespace OpsCopilot.BuildingBlocks.Contracts.AgentRuns;

/// <summary>
/// Optional normalized Azure scope/context metadata captured at alert-ingestion time
/// and persisted on the AgentRun row for downstream reporting/correlation.
/// </summary>
public sealed record AlertRunContext(
    string? AlertProvider = null,
    string? AlertSourceType = null,
    bool IsExceptionSignal = false,
    string? AzureSubscriptionId = null,
    string? AzureResourceGroup = null,
    string? AzureResourceId = null,
    string? AzureApplication = null,
    string? AzureWorkspaceId = null);
