namespace OpsCopilot.AgentRuns.Application.Abstractions;

public sealed record DeploymentDiffResponse(
    bool                                Ok,
    IReadOnlyList<DeploymentDiffChange> Changes,
    string                              SubscriptionId,
    DateTimeOffset                      ExecutedAtUtc,
    string?                             Error = null);

public sealed record DeploymentDiffChange(
    string         ResourceId,
    string         ResourceGroup,
    string         ChangeType,
    DateTimeOffset ChangeTime,
    string         Summary);
