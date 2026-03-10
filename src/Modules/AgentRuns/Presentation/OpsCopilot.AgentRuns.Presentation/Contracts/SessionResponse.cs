namespace OpsCopilot.AgentRuns.Presentation.Contracts;

public sealed record SessionRunSummaryDto(
    Guid           RunId,
    string         Status,
    string?        AlertFingerprint,
    DateTimeOffset CreatedAtUtc);

public sealed record SessionResponse(
    Guid                                    SessionId,
    string                                  TenantId,
    bool                                    IsExpired,
    DateTimeOffset                          CreatedAtUtc,
    DateTimeOffset                          ExpiresAtUtc,
    IReadOnlyList<SessionRunSummaryDto>     RecentRuns);
