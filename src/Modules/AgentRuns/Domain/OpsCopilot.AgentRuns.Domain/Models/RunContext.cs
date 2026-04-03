namespace OpsCopilot.AgentRuns.Domain.Models;

public sealed record RunContext(
    string? AlertProvider = null,
    string? AlertSourceType = null,
    bool IsExceptionSignal = false,
    string? AzureSubscriptionId = null,
    string? AzureResourceGroup = null,
    string? AzureResourceId = null,
    string? AzureApplication = null,
    string? AzureWorkspaceId = null);
