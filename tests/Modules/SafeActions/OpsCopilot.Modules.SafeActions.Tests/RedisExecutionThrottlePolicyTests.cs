using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Presentation.Throttling;
using StackExchange.Redis;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Verifies <see cref="RedisExecutionThrottlePolicy"/> distributed
/// fixed-window throttle behavior via mocked <see cref="IConnectionMultiplexer"/>.
/// Covers: disabled config, under-limit, over-limit, Redis fail-open, and expiry call.
/// Slice 196 — SafeActions Redis Execution Throttle Policy.
/// </summary>
public class RedisExecutionThrottlePolicyTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(
        bool enabled = true, int windowSeconds = 60, int maxAttempts = 5)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecutionThrottling"]             = enabled.ToString(),
                ["SafeActions:ExecutionThrottleWindowSeconds"]        = windowSeconds.ToString(),
                ["SafeActions:ExecutionThrottleMaxAttemptsPerWindow"] = maxAttempts.ToString(),
            })
            .Build();
    }

    private static (RedisExecutionThrottlePolicy Policy, Mock<IDatabase> MockDb) BuildPolicy(
        IConfiguration config,
        long incrementResult,
        TimeSpan? ttlResult = null)
    {
        var mockDb  = new Mock<IDatabase>();
        var mockMux = new Mock<IConnectionMultiplexer>();

        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
               .Returns(mockDb.Object);

        mockDb.Setup(d => d.StringIncrement(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
              .Returns(incrementResult);

        mockDb.Setup(d => d.KeyExpire(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
              .Returns(true);

        mockDb.Setup(d => d.KeyTimeToLive(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .Returns(ttlResult);

        var policy = new RedisExecutionThrottlePolicy(
            mockMux.Object,
            config,
            NullLogger<RedisExecutionThrottlePolicy>.Instance);

        return (policy, mockDb);
    }

    // ─── 1. Throttling disabled → always Allow, no Redis calls ───────

    [Fact]
    public void Evaluate_ThrottlingDisabled_ReturnsAllow_WithoutCallingRedis()
    {
        var mockMux = new Mock<IConnectionMultiplexer>();
        var config  = BuildConfig(enabled: false);

        var policy = new RedisExecutionThrottlePolicy(
            mockMux.Object,
            config,
            NullLogger<RedisExecutionThrottlePolicy>.Instance);

        var result = policy.Evaluate("tenant-1", "restart_pod", "execute");

        Assert.True(result.Allowed);
        Assert.Null(result.ReasonCode);

        // Redis must not be contacted at all when throttling is disabled.
        mockMux.Verify(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }

    // ─── 2. Under the limit → Allow ───────────────────────────────────

    [Fact]
    public void Evaluate_CountUnderLimit_ReturnsAllow()
    {
        var config = BuildConfig(maxAttempts: 5);
        var (policy, _) = BuildPolicy(config, incrementResult: 3);  // 3 ≤ 5

        var result = policy.Evaluate("tenant-1", "restart_pod", "execute");

        Assert.True(result.Allowed);
        Assert.Null(result.ReasonCode);
    }

    // ─── 3. Over the limit → Deny with retry-after from TTL ──────────

    [Fact]
    public void Evaluate_CountOverLimit_ReturnsDeny_WithRetryAfterFromTtl()
    {
        var config = BuildConfig(maxAttempts: 5);
        var (policy, _) = BuildPolicy(
            config,
            incrementResult: 6,                     // 6 > 5 → denied
            ttlResult:       TimeSpan.FromSeconds(42));

        var result = policy.Evaluate("tenant-2", "scale_deployment", "execute");

        Assert.False(result.Allowed);
        Assert.Equal("TooManyRequests", result.ReasonCode);
        Assert.Equal(42, result.RetryAfterSeconds);
    }

    // ─── 4. Redis throws → fail-open (Allow) ─────────────────────────

    [Fact]
    public void Evaluate_RedisThrows_ReturnsAllow_FailOpen()
    {
        var mockDb  = new Mock<IDatabase>();
        var mockMux = new Mock<IConnectionMultiplexer>();

        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
               .Returns(mockDb.Object);

        mockDb.Setup(d => d.StringIncrement(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
              .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is down"));

        var policy = new RedisExecutionThrottlePolicy(
            mockMux.Object,
            BuildConfig(),
            NullLogger<RedisExecutionThrottlePolicy>.Instance);

        var result = policy.Evaluate("tenant-3", "restart_pod", "execute");

        // Fail-open: a Redis outage must not block executions.
        Assert.True(result.Allowed);
    }

    // ─── 5. First increment (count=1) → KeyExpire is called ──────────

    [Fact]
    public void Evaluate_FirstIncrement_CallsKeyExpire()
    {
        var config        = BuildConfig(windowSeconds: 120, maxAttempts: 5);
        var (policy, mockDb) = BuildPolicy(config, incrementResult: 1);

        policy.Evaluate("tenant-4", "drain_node", "execute");

        // When count == 1 the policy must set the window expiry.
        mockDb.Verify(
            d => d.KeyExpire(It.IsAny<RedisKey>(), TimeSpan.FromSeconds(120), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ─── 6. Subsequent increments (count > 1) → KeyExpire NOT called ─

    [Fact]
    public void Evaluate_SubsequentIncrement_DoesNotCallKeyExpire()
    {
        var config           = BuildConfig(maxAttempts: 5);
        var (policy, mockDb) = BuildPolicy(config, incrementResult: 2);

        policy.Evaluate("tenant-5", "rollback", "rollback_execute");

        // Expiry is only set on the first call; the key already has a TTL.
        mockDb.Verify(
            d => d.KeyExpire(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }
}
