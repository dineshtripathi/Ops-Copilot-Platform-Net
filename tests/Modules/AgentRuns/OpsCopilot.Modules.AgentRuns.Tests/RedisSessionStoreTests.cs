using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Infrastructure.Sessions;
using StackExchange.Redis;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class RedisSessionStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDb;
    private readonly Mock<TimeProvider> _mockTime;
    private readonly Mock<ILogger<RedisSessionStore>> _mockLogger;
    private readonly DateTimeOffset _fixedNow;
    private readonly RedisSessionStore _sut;

    public RedisSessionStoreTests()
    {
        _mockRedis  = new Mock<IConnectionMultiplexer>();
        _mockDb     = new Mock<IDatabase>();
        _mockTime   = new Mock<TimeProvider>();
        _mockLogger = new Mock<ILogger<RedisSessionStore>>();

        _fixedNow = new DateTimeOffset(2025, 7, 15, 12, 0, 0, TimeSpan.Zero);
        _mockTime.Setup(t => t.GetUtcNow()).Returns(_fixedNow);

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_mockDb.Object);

        _sut = new RedisSessionStore(
            _mockRedis.Object,
            _mockTime.Object,
            _mockLogger.Object);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HashEntry[] CreateHashEntries(
        Guid sessionId, string tenantId, DateTimeOffset createdAt, DateTimeOffset expiresAt) =>
    [
        new("sessionId",    sessionId.ToString()),
        new("tenantId",     tenantId),
        new("createdAtUtc", createdAt.ToString("O")),
        new("expiresAtUtc", expiresAt.ToString("O")),
    ];

    private static string LookupKey(Guid sessionId) =>
        $"opscopilot:agentruns:sessions:lookup:{sessionId}";

    private static string ShadowKey(Guid sessionId) =>
        $"opscopilot:agentruns:sessions:shadow:{sessionId}";

    private static string PrimaryKey(string tenantId, Guid sessionId) =>
        $"opscopilot:agentruns:sessions:{tenantId}:{sessionId}";

    private Mock<IBatch> SetupBatch()
    {
        var mockBatch = new Mock<IBatch>();

        // HashSetAsync — multi-field (returns Task)
        mockBatch
            .Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        // HashSetAsync — single-field (returns Task<bool>)
        mockBatch
            .Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // KeyExpireAsync — both overloads (SE.Redis 2.8+ has ExpireWhen variant)
        mockBatch
            .Setup(b => b.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockBatch
            .Setup(b => b.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // StringSetAsync
        mockBatch
            .Setup(b => b.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDb
            .Setup(d => d.CreateBatch(It.IsAny<object?>()))
            .Returns(mockBatch.Object);

        return mockBatch;
    }

    // ── Constructor guard tests ──────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenRedisIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new RedisSessionStore(null!, _mockTime.Object, _mockLogger.Object));
        Assert.Equal("redis", ex.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenTimeProviderIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new RedisSessionStore(_mockRedis.Object, null!, _mockLogger.Object));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new RedisSessionStore(_mockRedis.Object, _mockTime.Object, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    // ── GetAsync tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenLookupKeyDoesNotExist()
    {
        var sessionId = Guid.NewGuid();

        _mockDb
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _sut.GetAsync(sessionId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenHashExpiredBetweenLookupAndFetch()
    {
        var sessionId = Guid.NewGuid();
        var tenantId  = "tenant-abc";
        var fullKey   = PrimaryKey(tenantId, sessionId);

        // Lookup key returns the full key — session existed recently.
        _mockDb
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)fullKey);

        // But HashGetAll returns empty — the hash has expired between calls.
        _mockDb
            .Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k == (RedisKey)fullKey),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // KeyDeleteAsync for cleanup of stale lookup key
        _mockDb
            .Setup(d => d.KeyDeleteAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _sut.GetAsync(sessionId);

        Assert.Null(result);
        _mockDb.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsSessionInfo_WhenSessionExists()
    {
        var sessionId = Guid.NewGuid();
        var tenantId  = "tenant-ok";
        var fullKey   = PrimaryKey(tenantId, sessionId);
        var createdAt = _fixedNow.AddMinutes(-5);
        var expiresAt = _fixedNow.AddMinutes(25);

        _mockDb
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)fullKey);

        _mockDb
            .Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k == (RedisKey)fullKey),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(CreateHashEntries(sessionId, tenantId, createdAt, expiresAt));

        var result = await _sut.GetAsync(sessionId);

        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(createdAt, result.CreatedAtUtc);
        Assert.Equal(expiresAt, result.ExpiresAtUtc);
        Assert.False(result.IsNew); // Fetched from store → never IsNew
    }

    // ── GetIncludingExpiredAsync tests ───────────────────────────────────────

    [Fact]
    public async Task GetIncludingExpiredAsync_ReturnsNull_WhenSessionWasNeverCreated()
    {
        var sessionId = Guid.NewGuid();

        _mockDb
            .Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k == (RedisKey)ShadowKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        var result = await _sut.GetIncludingExpiredAsync(sessionId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetIncludingExpiredAsync_ReturnsSessionInfo_FromShadowKey()
    {
        var sessionId = Guid.NewGuid();
        var tenantId  = "tenant-shadow";
        var createdAt = _fixedNow.AddHours(-2);
        var expiresAt = _fixedNow.AddMinutes(-30); // Already expired

        _mockDb
            .Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k == (RedisKey)ShadowKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(CreateHashEntries(sessionId, tenantId, createdAt, expiresAt));

        var result = await _sut.GetIncludingExpiredAsync(sessionId);

        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.False(result.IsNew);
        // The session IS expired, but the shadow key still returns it.
        Assert.True(result.ExpiresAtUtc < _fixedNow);
    }

    // ── CreateAsync tests ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsSessionInfo_WithIsNewTrue()
    {
        var tenantId = "tenant-create";
        var ttl      = TimeSpan.FromMinutes(30);
        SetupBatch();

        var result = await _sut.CreateAsync(tenantId, ttl);

        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(_fixedNow, result.CreatedAtUtc);
        Assert.Equal(_fixedNow.Add(ttl), result.ExpiresAtUtc);
        Assert.True(result.IsNew);
    }

    [Fact]
    public async Task CreateAsync_PipelinesAllRedisOperationsViaBatch()
    {
        var tenantId  = "tenant-batch";
        var ttl       = TimeSpan.FromMinutes(20);
        var mockBatch = SetupBatch();

        await _sut.CreateAsync(tenantId, ttl);

        // Primary hash + shadow hash = 2 multi-field HashSet calls
        mockBatch.Verify(
            b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()),
            Times.Exactly(2));

        // KeyExpire on primary key (1 call)
        var keyExpireCalls = mockBatch.Invocations
            .Where(i => i.Method.Name == nameof(IBatch.KeyExpireAsync))
            .ToList();
        Assert.Single(keyExpireCalls);
        Assert.Equal(ttl, (TimeSpan?)keyExpireCalls[0].Arguments[1]);

        // StringSet for lookup key
        mockBatch.Verify(
            b => b.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.Is<TimeSpan?>(t => t == ttl),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);

        // Execute called once
        mockBatch.Verify(b => b.Execute(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_UsesCorrectKeyFormat_WithTenantAndSessionId()
    {
        var tenantId  = "tenant-keyformat";
        var ttl       = TimeSpan.FromMinutes(15);
        var mockBatch = SetupBatch();

        var capturedKeys = new List<string>();
        mockBatch
            .Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((key, _, _) =>
                capturedKeys.Add(key.ToString()))
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateAsync(tenantId, ttl);

        // Primary key: opscopilot:agentruns:sessions:{tenantId}:{sessionId}
        var expectedPrimary = $"opscopilot:agentruns:sessions:{tenantId}:{result.SessionId}";
        Assert.Contains(expectedPrimary, capturedKeys);

        // Shadow key: opscopilot:agentruns:sessions:shadow:{sessionId}
        var expectedShadow = $"opscopilot:agentruns:sessions:shadow:{result.SessionId}";
        Assert.Contains(expectedShadow, capturedKeys);
    }

    // ── TouchAsync tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task TouchAsync_ExtendsExpiry_WhenSessionExists()
    {
        var sessionId = Guid.NewGuid();
        var tenantId  = "tenant-touch";
        var fullKey   = PrimaryKey(tenantId, sessionId);
        var ttl       = TimeSpan.FromMinutes(30);
        var mockBatch = SetupBatch();

        // Lookup returns full key — session exists
        _mockDb
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)fullKey);

        await _sut.TouchAsync(sessionId, ttl);

        // KeyExpire on primary + lookup = 2 calls
        var keyExpireCalls = mockBatch.Invocations
            .Where(i => i.Method.Name == nameof(IBatch.KeyExpireAsync))
            .ToList();
        Assert.Equal(2, keyExpireCalls.Count);
        Assert.All(keyExpireCalls, call => Assert.Equal(ttl, (TimeSpan?)call.Arguments[1]));

        // HashSet single-field for expiresAtUtc on primary + shadow = 2 calls
        mockBatch.Verify(
            b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Exactly(2));

        mockBatch.Verify(b => b.Execute(), Times.Once);
    }

    [Fact]
    public async Task TouchAsync_IsNoOp_WhenSessionNotFound()
    {
        var sessionId = Guid.NewGuid();
        var ttl       = TimeSpan.FromMinutes(30);

        _mockDb
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k == (RedisKey)LookupKey(sessionId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        await _sut.TouchAsync(sessionId, ttl);

        // No batch should be created since session was not found
        _mockDb.Verify(
            d => d.CreateBatch(It.IsAny<object?>()), Times.Never);
    }

    // ── Tenant isolation test ────────────────────────────────────────────────

    [Theory]
    [InlineData("tenant-alpha")]
    [InlineData("tenant-beta")]
    [InlineData("org/team-42")]
    public async Task CreateAsync_KeyContainsTenantId_ForTenantIsolation(string tenantId)
    {
        var ttl       = TimeSpan.FromMinutes(10);
        var mockBatch = SetupBatch();

        var capturedPrimaryKey = string.Empty;
        mockBatch
            .Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((key, _, _) =>
            {
                var k = key.ToString();
                if (!k.Contains("shadow"))
                    capturedPrimaryKey = k;
            })
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateAsync(tenantId, ttl);

        Assert.StartsWith($"opscopilot:agentruns:sessions:{tenantId}:", capturedPrimaryKey);
        Assert.EndsWith(result.SessionId.ToString(), capturedPrimaryKey);
    }
}
