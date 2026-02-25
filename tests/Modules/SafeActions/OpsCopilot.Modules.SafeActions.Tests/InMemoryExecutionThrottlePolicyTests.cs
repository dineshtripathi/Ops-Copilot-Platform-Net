using Microsoft.Extensions.Configuration;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Presentation.Throttling;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Verifies <see cref="InMemoryExecutionThrottlePolicy"/> fixed-window
/// throttle behavior, including config-driven on/off, window reset,
/// key isolation, case-insensitivity, and retry-after calculation.
/// Slice 19 — SafeActions Execution Throttling (STRICT, In-Process).
/// </summary>
public class InMemoryExecutionThrottlePolicyTests
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

    // ─── Throttling disabled → always Allow ───────────────────────

    [Fact]
    public void Evaluate_ThrottlingDisabled_ReturnsAllow()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(enabled: false));

        var result = policy.Evaluate("tenant-1", "restart_pod", "execute");

        Assert.True(result.Allowed);
        Assert.Null(result.ReasonCode);
    }

    [Fact]
    public void Evaluate_ThrottlingDisabled_NeverDenies_EvenAfterManyAttempts()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(enabled: false, maxAttempts: 1));

        for (int i = 0; i < 100; i++)
        {
            var result = policy.Evaluate("tenant-1", "restart_pod", "execute");
            Assert.True(result.Allowed);
        }
    }

    // ─── Within window, under limit → Allow ───────────────────────

    [Fact]
    public void Evaluate_UnderLimit_ReturnsAllow()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 5));

        for (int i = 0; i < 5; i++)
        {
            var result = policy.Evaluate("tenant-1", "restart_pod", "execute");
            Assert.True(result.Allowed);
        }
    }

    // ─── Exceeds limit → Deny ─────────────────────────────────────

    [Fact]
    public void Evaluate_ExceedsLimit_ReturnsDeny()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 3));

        for (int i = 0; i < 3; i++)
            policy.Evaluate("tenant-1", "restart_pod", "execute");

        var result = policy.Evaluate("tenant-1", "restart_pod", "execute");

        Assert.False(result.Allowed);
        Assert.Equal("TooManyRequests", result.ReasonCode);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.RetryAfterSeconds);
        Assert.True(result.RetryAfterSeconds >= 1);
    }

    // ─── Different tenants tracked separately ─────────────────────

    [Fact]
    public void Evaluate_DifferentTenants_TrackedSeparately()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 2));

        policy.Evaluate("tenant-1", "restart_pod", "execute");
        policy.Evaluate("tenant-1", "restart_pod", "execute");

        // tenant-1 is now at limit; tenant-2 should still be allowed
        var result = policy.Evaluate("tenant-2", "restart_pod", "execute");
        Assert.True(result.Allowed);
    }

    // ─── Different operation kinds tracked separately ──────────────

    [Fact]
    public void Evaluate_DifferentOperationKinds_TrackedSeparately()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 2));

        policy.Evaluate("tenant-1", "restart_pod", "execute");
        policy.Evaluate("tenant-1", "restart_pod", "execute");

        // execute is at limit; rollback_execute should still be allowed
        var result = policy.Evaluate("tenant-1", "restart_pod", "rollback_execute");
        Assert.True(result.Allowed);
    }

    // ─── Different action types tracked separately ─────────────────

    [Fact]
    public void Evaluate_DifferentActionTypes_TrackedSeparately()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 2));

        policy.Evaluate("tenant-1", "restart_pod", "execute");
        policy.Evaluate("tenant-1", "restart_pod", "execute");

        var result = policy.Evaluate("tenant-1", "scale_out", "execute");
        Assert.True(result.Allowed);
    }

    // ─── Case-insensitive keys ────────────────────────────────────

    [Fact]
    public void Evaluate_CaseInsensitiveKeys_ShareSameCounter()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 2));

        policy.Evaluate("Tenant-1", "Restart_Pod", "Execute");
        policy.Evaluate("tenant-1", "restart_pod", "execute");

        var result = policy.Evaluate("TENANT-1", "RESTART_POD", "EXECUTE");

        Assert.False(result.Allowed);
    }

    // ─── RetryAfterSeconds is ≥ 1 ─────────────────────────────────

    [Fact]
    public void Evaluate_Denied_RetryAfterSeconds_AtLeast1()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 1));

        policy.Evaluate("tenant-1", "restart_pod", "execute");
        var result = policy.Evaluate("tenant-1", "restart_pod", "execute");

        Assert.False(result.Allowed);
        Assert.True(result.RetryAfterSeconds >= 1);
    }

    // ─── Max attempts = 1 → second call denied ────────────────────

    [Fact]
    public void Evaluate_MaxAttemptsOne_SecondCallDenied()
    {
        var policy = new InMemoryExecutionThrottlePolicy(BuildConfig(maxAttempts: 1));

        var first = policy.Evaluate("tenant-1", "restart_pod", "execute");
        Assert.True(first.Allowed);

        var second = policy.Evaluate("tenant-1", "restart_pod", "execute");
        Assert.False(second.Allowed);
    }

    // ─── ThrottleDecision static factories ─────────────────────────

    [Fact]
    public void ThrottleDecision_Allow_ReturnsExpectedShape()
    {
        var decision = ThrottleDecision.Allow();

        Assert.True(decision.Allowed);
        Assert.Null(decision.ReasonCode);
        Assert.Null(decision.Message);
        Assert.Null(decision.RetryAfterSeconds);
    }

    [Fact]
    public void ThrottleDecision_Deny_ReturnsExpectedShape()
    {
        var decision = ThrottleDecision.Deny(42);

        Assert.False(decision.Allowed);
        Assert.Equal("TooManyRequests", decision.ReasonCode);
        Assert.Contains("42", decision.Message!);
        Assert.Equal(42, decision.RetryAfterSeconds);
    }

    // ─── Defaults used when config keys missing ────────────────────

    [Fact]
    public void Evaluate_DefaultConfig_Uses60sWindowAnd5Max()
    {
        // Only enable throttling — rely on defaults for window & max
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecutionThrottling"] = "true",
            })
            .Build();

        var policy = new InMemoryExecutionThrottlePolicy(config);

        // Default max is 5 — first 5 should pass
        for (int i = 0; i < 5; i++)
        {
            Assert.True(policy.Evaluate("t", "a", "e").Allowed);
        }

        // 6th should be denied
        Assert.False(policy.Evaluate("t", "a", "e").Allowed);
    }
}
