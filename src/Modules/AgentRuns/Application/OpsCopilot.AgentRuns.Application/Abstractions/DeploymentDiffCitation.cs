namespace OpsCopilot.AgentRuns.Application.Abstractions;

public sealed record DeploymentDiffCitation(
    string         SubscriptionId,
    string         ResourceGroup,
    string         ResourceId,
    string         ChangeType,
    DateTimeOffset ChangeTime,
    string         Summary);
