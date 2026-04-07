using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;

namespace OpsCopilot.AgentRuns.Infrastructure.Sessions;

/// <summary>
/// SQL-backed session store using EF Core via <see cref="IServiceScopeFactory"/>.
/// Registered as Singleton; creates a new DbContext scope per operation
/// to satisfy EF Core's scoped-lifetime requirement from a singleton service.
/// </summary>
public sealed class SqlSessionStore : ISessionStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public SqlSessionStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
        var now = _timeProvider.GetUtcNow();
        var entry = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.ExpiresAtUtc > now, ct);
        return entry is null ? null : ToInfo(entry, isNew: false);
    }

    public async Task<SessionInfo?> GetIncludingExpiredAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
        var entry = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        return entry is null ? null : ToInfo(entry, isNew: false);
    }

    public async Task<SessionInfo> CreateAsync(string tenantId, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
        var now = _timeProvider.GetUtcNow();
        var entry = SessionEntry.Create(tenantId, now, now.Add(ttl));
        db.Sessions.Add(entry);
        await db.SaveChangesAsync(ct);
        return ToInfo(entry, isNew: true);
    }

    public async Task TouchAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentRunsDbContext>();
        var entry = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (entry is null) return;
        entry.Touch(_timeProvider.GetUtcNow().Add(ttl));
        await db.SaveChangesAsync(ct);
    }

    private static SessionInfo ToInfo(SessionEntry e, bool isNew)
        => new(e.SessionId, e.TenantId, e.CreatedAtUtc, e.ExpiresAtUtc, isNew);
}
