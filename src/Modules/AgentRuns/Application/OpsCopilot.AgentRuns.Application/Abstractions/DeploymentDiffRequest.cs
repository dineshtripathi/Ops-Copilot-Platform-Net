namespace OpsCopilot.AgentRuns.Application.Abstractions;

public sealed record DeploymentDiffRequest(
    string  TenantId,
    string  SubscriptionId,
    string? ResourceGroup,
    int     LookbackMinutes);
