namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Stores and retrieves triage sessions.
/// MVP implementation: in-memory with TTL expiration.
/// Production: Redis or SQL-backed.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Returns the session if it exists and has not expired; otherwise <c>null</c>.
    /// </summary>
    Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new session for the given tenant, returning a snapshot with <c>IsNew = true</c>.
    /// </summary>
    Task<SessionInfo> CreateAsync(string tenantId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Returns the session regardless of expiry. Returns <c>null</c> only if the
    /// session ID was never created. Callers must inspect <c>ExpiresAtUtc</c>
    /// to determine whether the session has expired.
    /// </summary>
    Task<SessionInfo?> GetIncludingExpiredAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Extends the session expiry by touching it.
    /// No-op if the session does not exist or has already expired.
    /// </summary>
    Task TouchAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default);
}
