using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Presentation.Throttling;

/// <summary>
/// In-process, fixed-window execution throttle policy.
/// Tracks execution attempts per key (tenantId | actionType | operationKind)
/// within a configurable time window. Returns <see cref="ThrottleDecision.Deny"/>
/// when the maximum number of attempts per window is exceeded.
/// Single-node only — not distributed.
/// </summary>
public sealed class InMemoryExecutionThrottlePolicy : IExecutionThrottlePolicy
{
    private readonly IConfiguration _configuration;

    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _counters
        = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryExecutionThrottlePolicy(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ThrottleDecision Evaluate(string tenantId, string actionType, string operationKind)
    {
        if (!_configuration.GetValue<bool>("SafeActions:EnableExecutionThrottling"))
            return ThrottleDecision.Allow();

        var windowSeconds     = _configuration.GetValue("SafeActions:ExecutionThrottleWindowSeconds", 60);
        var maxPerWindow      = _configuration.GetValue("SafeActions:ExecutionThrottleMaxAttemptsPerWindow", 5);
        var now               = DateTimeOffset.UtcNow;
        var key               = $"{tenantId}|{actionType}|{operationKind}";

        var updated = _counters.AddOrUpdate(
            key,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart >= TimeSpan.FromSeconds(windowSeconds))
                    return (1, now);              // window expired — reset

                return (existing.Count + 1, existing.WindowStart);
            });

        if (updated.Count > maxPerWindow)
        {
            var elapsed         = now - updated.WindowStart;
            var retryAfterSeconds = Math.Max(1, windowSeconds - (int)elapsed.TotalSeconds);
            return ThrottleDecision.Deny(retryAfterSeconds);
        }

        return ThrottleDecision.Allow();
    }
}
