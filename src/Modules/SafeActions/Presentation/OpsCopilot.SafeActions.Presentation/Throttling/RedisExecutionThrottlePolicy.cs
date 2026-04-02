using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;
using StackExchange.Redis;

namespace OpsCopilot.SafeActions.Presentation.Throttling;

/// <summary>
/// Distributed, Redis-backed execution throttle policy using the fixed-window
/// INCR + EXPIRE pattern.  Each unique (tenantId, actionType, operationKind)
/// tuple gets its own Redis key that automatically expires at the end of the window.
///
/// Key format: <c>opscopilot:safeactions:throttle:{tenantId}:{actionType}:{operationKind}</c>
///
/// <para>
/// Fail-open: if Redis is unreachable the method logs a warning and returns
/// <see cref="ThrottleDecision.Allow"/>, so a transient cache outage does not
/// block legitimate operator actions.
/// </para>
/// </summary>
public sealed class RedisExecutionThrottlePolicy : IExecutionThrottlePolicy
{
    private const string KeyPrefix = "opscopilot:safeactions:throttle";

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration         _configuration;
    private readonly ILogger<RedisExecutionThrottlePolicy> _logger;

    public RedisExecutionThrottlePolicy(
        IConnectionMultiplexer redis,
        IConfiguration         configuration,
        ILogger<RedisExecutionThrottlePolicy> logger)
    {
        _redis         = redis         ?? throw new ArgumentNullException(nameof(redis));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger        = logger        ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ThrottleDecision Evaluate(string tenantId, string actionType, string operationKind)
    {
        if (!_configuration.GetValue<bool>("SafeActions:EnableExecutionThrottling"))
            return ThrottleDecision.Allow();

        var windowSeconds = _configuration.GetValue("SafeActions:ExecutionThrottleWindowSeconds",        60);
        var maxPerWindow  = _configuration.GetValue("SafeActions:ExecutionThrottleMaxAttemptsPerWindow", 5);

        try
        {
            var db  = _redis.GetDatabase();
            var key = (RedisKey)$"{KeyPrefix}:{tenantId}:{actionType}:{operationKind}";

            var count = db.StringIncrement(key);

            if (count == 1)
            {
                // First attempt in the current window — set the sliding expiry.
                db.KeyExpire(key, TimeSpan.FromSeconds(windowSeconds));
            }

            if (count > maxPerWindow)
            {
                var ttl               = db.KeyTimeToLive(key);
                var retryAfterSeconds = ttl.HasValue
                    ? Math.Max(1, (int)ttl.Value.TotalSeconds)
                    : windowSeconds;

                return ThrottleDecision.Deny(retryAfterSeconds);
            }

            return ThrottleDecision.Allow();
        }
        catch (Exception ex)
        {
            // Fail-open: a Redis outage must not block legitimate executions.
            _logger.LogWarning(ex,
                "Redis throttle check failed for {TenantId}|{ActionType}|{OperationKind}. Allowing execution (fail-open).",
                tenantId, actionType, operationKind);

            return ThrottleDecision.Allow();
        }
    }
}
