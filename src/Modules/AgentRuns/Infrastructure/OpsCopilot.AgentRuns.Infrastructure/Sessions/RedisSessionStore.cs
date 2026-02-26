using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using StackExchange.Redis;

namespace OpsCopilot.AgentRuns.Infrastructure.Sessions;

/// <summary>
/// Redis-backed session store for production / multi-replica deployments.
/// Each session is stored as a Redis hash with a key TTL for automatic expiry.
///
/// Key format: <c>opscopilot:agentruns:sessions:{tenantId}:{sessionId}</c>
///
/// Hash fields:
///   sessionId   – GUID string
///   tenantId    – tenant identifier
///   createdAtUtc– ISO-8601 DateTimeOffset
///   expiresAtUtc– ISO-8601 DateTimeOffset (logical; Redis TTL is authoritative)
///
/// Sliding expiration is achieved by resetting the Redis key TTL on every
/// <see cref="TouchAsync"/> call, mirroring the in-memory store behaviour.
/// </summary>
public sealed class RedisSessionStore : ISessionStore
{
    private const string KeyPrefix = "opscopilot:agentruns:sessions";

    // Hash field names
    private const string FieldSessionId   = "sessionId";
    private const string FieldTenantId    = "tenantId";
    private const string FieldCreatedAt   = "createdAtUtc";
    private const string FieldExpiresAt   = "expiresAtUtc";

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedisSessionStore> _logger;

    public RedisSessionStore(
        IConnectionMultiplexer redis,
        TimeProvider timeProvider,
        ILogger<RedisSessionStore> logger)
    {
        _redis        = redis ?? throw new ArgumentNullException(nameof(redis));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger       = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // We must scan for the key because we only know sessionId, not tenantId.
        // However, the key contains tenantId. We store a reverse-lookup key
        // "opscopilot:agentruns:sessions:lookup:{sessionId}" → full key.
        var fullKey = await GetFullKeyAsync(db, sessionId).ConfigureAwait(false);
        if (fullKey.IsNull)
        {
            _logger.LogDebug("Session {SessionId}: not found in Redis (no lookup key)", sessionId);
            return null;
        }

        var hash = await db.HashGetAllAsync(fullKey.ToString()).ConfigureAwait(false);
        if (hash.Length == 0)
        {
            // Key expired between lookup and fetch — clean up stale lookup.
            await RemoveLookupKeyAsync(db, sessionId).ConfigureAwait(false);
            _logger.LogDebug("Session {SessionId}: lookup existed but hash expired", sessionId);
            return null;
        }

        var session = FromHash(hash);

        // Redis TTL is authoritative; if the key still exists, the session is valid.
        _logger.LogDebug("Session {SessionId}: found in Redis for tenant {TenantId}", sessionId, session.TenantId);
        return session;
    }

    /// <inheritdoc />
    public async Task<SessionInfo?> GetIncludingExpiredAsync(Guid sessionId, CancellationToken ct = default)
    {
        // For Redis, once a key expires it's gone. We cannot retrieve expired
        // sessions. To support the "get including expired" contract we store a
        // shadow key with no TTL that holds the session metadata.
        var db = _redis.GetDatabase();
        var shadowKey = ShadowKey(sessionId);

        var hash = await db.HashGetAllAsync(shadowKey).ConfigureAwait(false);
        if (hash.Length == 0)
        {
            _logger.LogDebug("Session {SessionId}: no shadow key — never created", sessionId);
            return null;
        }

        var session = FromHash(hash);
        _logger.LogDebug("Session {SessionId}: shadow key found (may be expired)", sessionId);
        return session;
    }

    /// <inheritdoc />
    public async Task<SessionInfo> CreateAsync(string tenantId, TimeSpan ttl, CancellationToken ct = default)
    {
        var now       = _timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid();
        var expiresAt = now.Add(ttl);

        var db      = _redis.GetDatabase();
        var key     = Key(tenantId, sessionId);
        var entries = ToHashEntries(sessionId, tenantId, now, expiresAt);

        // Pipeline: set hash + set TTL + set lookup + set shadow
        var batch = db.CreateBatch();

        var setTask    = batch.HashSetAsync(key, entries);
        var expireTask = batch.KeyExpireAsync(key, ttl);
        var lookupTask = batch.StringSetAsync(LookupKey(sessionId), key, ttl);
        // Shadow key — no TTL so GetIncludingExpired works after expiry.
        var shadowTask = batch.HashSetAsync(ShadowKey(sessionId), entries);

        batch.Execute();
        await Task.WhenAll(setTask, expireTask, lookupTask, shadowTask).ConfigureAwait(false);

        _logger.LogInformation(
            "Session {SessionId}: created in Redis for tenant {TenantId}, TTL {TtlMinutes}m",
            sessionId, tenantId, ttl.TotalMinutes);

        return new SessionInfo(sessionId, tenantId, now, expiresAt, IsNew: true);
    }

    /// <inheritdoc />
    public async Task TouchAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        var db      = _redis.GetDatabase();
        var fullKey = await GetFullKeyAsync(db, sessionId).ConfigureAwait(false);
        if (fullKey.IsNull)
        {
            _logger.LogDebug("Session {SessionId}: touch skipped — not found", sessionId);
            return;
        }

        var key        = fullKey.ToString();
        var now        = _timeProvider.GetUtcNow();
        var newExpiry  = now.Add(ttl);

        // Pipeline: extend TTL on primary key + lookup key + update expiresAtUtc
        var batch = db.CreateBatch();

        var expireTask       = batch.KeyExpireAsync(key, ttl);
        var lookupExpireTask = batch.KeyExpireAsync(LookupKey(sessionId), ttl);
        var updateExpiryTask = batch.HashSetAsync(key, FieldExpiresAt, newExpiry.ToString("O"));
        // Also update shadow so GetIncludingExpired returns latest expiry
        var shadowExpiryTask = batch.HashSetAsync(ShadowKey(sessionId), FieldExpiresAt, newExpiry.ToString("O"));

        batch.Execute();
        await Task.WhenAll(expireTask, lookupExpireTask, updateExpiryTask, shadowExpiryTask).ConfigureAwait(false);

        _logger.LogDebug("Session {SessionId}: touched — new expiry {ExpiresAtUtc}", sessionId, newExpiry);
    }

    // ── Key helpers ──────────────────────────────────────────────────────────

    /// <summary>Primary session key: <c>opscopilot:agentruns:sessions:{tenantId}:{sessionId}</c></summary>
    private static string Key(string tenantId, Guid sessionId)
        => $"{KeyPrefix}:{tenantId}:{sessionId}";

    /// <summary>Reverse-lookup key to find full key by sessionId alone.</summary>
    private static string LookupKey(Guid sessionId)
        => $"{KeyPrefix}:lookup:{sessionId}";

    /// <summary>Shadow key (no TTL) for GetIncludingExpired support.</summary>
    private static string ShadowKey(Guid sessionId)
        => $"{KeyPrefix}:shadow:{sessionId}";

    private static async Task<RedisValue> GetFullKeyAsync(IDatabase db, Guid sessionId)
        => await db.StringGetAsync(LookupKey(sessionId)).ConfigureAwait(false);

    private static async Task RemoveLookupKeyAsync(IDatabase db, Guid sessionId)
        => await db.KeyDeleteAsync(LookupKey(sessionId)).ConfigureAwait(false);

    // ── Hash serialization ───────────────────────────────────────────────────

    private static HashEntry[] ToHashEntries(
        Guid sessionId, string tenantId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        return
        [
            new(FieldSessionId, sessionId.ToString()),
            new(FieldTenantId,  tenantId),
            new(FieldCreatedAt, createdAt.ToString("O")),
            new(FieldExpiresAt, expiresAt.ToString("O")),
        ];
    }

    private static SessionInfo FromHash(HashEntry[] entries)
    {
        string? sid = null, tid = null, cat = null, eat = null;

        foreach (var e in entries)
        {
            switch (e.Name.ToString())
            {
                case FieldSessionId: sid = e.Value!; break;
                case FieldTenantId:  tid = e.Value!; break;
                case FieldCreatedAt: cat = e.Value!; break;
                case FieldExpiresAt: eat = e.Value!; break;
            }
        }

        return new SessionInfo(
            SessionId:    Guid.Parse(sid!),
            TenantId:     tid!,
            CreatedAtUtc: DateTimeOffset.Parse(cat!),
            ExpiresAtUtc: DateTimeOffset.Parse(eat!),
            IsNew:        false);
    }
}
