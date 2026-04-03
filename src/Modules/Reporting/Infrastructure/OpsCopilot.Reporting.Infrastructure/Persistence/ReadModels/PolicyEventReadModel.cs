namespace OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

internal sealed class PolicyEventReadModel
{
    public Guid PolicyEventId { get; init; }
    public Guid RunId { get; init; }
    public string PolicyName { get; init; } = string.Empty;
    public bool Allowed { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}
