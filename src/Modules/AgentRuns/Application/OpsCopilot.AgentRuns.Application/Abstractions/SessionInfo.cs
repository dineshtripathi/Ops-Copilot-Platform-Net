namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Immutable snapshot of a triage session.
/// </summary>
public sealed record SessionInfo(
    Guid            SessionId,
    string          TenantId,
    DateTimeOffset  CreatedAtUtc,
    DateTimeOffset  ExpiresAtUtc,
    bool            IsNew);
