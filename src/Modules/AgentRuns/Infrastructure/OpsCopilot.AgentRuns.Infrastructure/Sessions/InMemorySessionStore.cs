using System.Collections.Concurrent;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.Sessions;

/// <summary>
/// In-memory session store for local development and MVP.
/// Production workloads should use a Redis or SQL-backed implementation.
/// Expired entries are lazily evicted on access.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<Guid, SessionEntry> _sessions = new();
    private readonly TimeProvider _timeProvider;

    public InMemorySessionStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            if (entry.ExpiresAtUtc > _timeProvider.GetUtcNow())
            {
                return Task.FromResult<SessionInfo?>(
                    new SessionInfo(entry.SessionId, entry.TenantId, entry.CreatedAtUtc, entry.ExpiresAtUtc, IsNew: false));
            }

            // Lazily evict expired entry
            _sessions.TryRemove(sessionId, out _);
        }

        return Task.FromResult<SessionInfo?>(null);
    }

    /// <inheritdoc />
    public Task<SessionInfo?> GetIncludingExpiredAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            return Task.FromResult<SessionInfo?>(
                new SessionInfo(entry.SessionId, entry.TenantId, entry.CreatedAtUtc, entry.ExpiresAtUtc, IsNew: false));
        }

        return Task.FromResult<SessionInfo?>(null);
    }

    public Task<SessionInfo> CreateAsync(string tenantId, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid();
        var expiresAt = now.Add(ttl);

        var entry = new SessionEntry(sessionId, tenantId, now, expiresAt);
        _sessions[sessionId] = entry;

        return Task.FromResult(
            new SessionInfo(sessionId, tenantId, now, expiresAt, IsNew: true));
    }

    public Task TouchAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            var extended = entry with { ExpiresAtUtc = _timeProvider.GetUtcNow().Add(ttl) };
            _sessions[sessionId] = extended;
        }

        return Task.CompletedTask;
    }

    private sealed record SessionEntry(
        Guid SessionId,
        string TenantId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
