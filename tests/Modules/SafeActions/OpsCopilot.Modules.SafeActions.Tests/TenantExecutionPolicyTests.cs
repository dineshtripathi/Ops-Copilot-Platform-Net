using Microsoft.Extensions.Configuration;
using OpsCopilot.SafeActions.Infrastructure.Policies;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="ConfigDrivenTenantExecutionPolicy"/>.
/// Strict / secure-by-default: missing config, missing key, or empty
/// tenant list all result in DENY.
/// </summary>
public sealed class TenantExecutionPolicyTests
{
    private const string ReasonCode = "tenant_not_authorized_for_action";

    // ── Helpers ─────────────────────────────────────────────────────

    private static ConfigDrivenTenantExecutionPolicy CreatePolicy(
        Dictionary<string, string?>? data = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data ?? [])
            .Build();
        return new ConfigDrivenTenantExecutionPolicy(config);
    }

    // ── Empty / missing config → DENY ───────────────────────────────

    [Fact]
    public void Empty_config_denies_any_action()
    {
        var policy = CreatePolicy();

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.False(result.Allowed);
        Assert.Equal(ReasonCode, result.ReasonCode);
    }

    [Fact]
    public void Missing_action_type_key_denies()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:http_probe:0"] = "t-1"
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.False(result.Allowed);
        Assert.Equal(ReasonCode, result.ReasonCode);
    }

    [Fact]
    public void Empty_tenant_array_denies()
    {
        // Section key exists but no child values
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod"] = null
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.False(result.Allowed);
        Assert.Equal(ReasonCode, result.ReasonCode);
    }

    // ── Tenant not in list → DENY ──────────────────────────────────

    [Fact]
    public void Tenant_not_in_allowlist_denies()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"] = "t-other"
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.False(result.Allowed);
        Assert.Equal(ReasonCode, result.ReasonCode);
    }

    // ── Tenant in list → ALLOW ─────────────────────────────────────

    [Fact]
    public void Tenant_in_allowlist_allows()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"] = "t-1"
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Multiple_tenants_in_allowlist_allows()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"] = "t-1",
            ["SafeActions:AllowedExecutionTenants:restart_pod:1"] = "t-2"
        });

        Assert.True(policy.EvaluateExecution("t-1", "restart_pod").Allowed);
        Assert.True(policy.EvaluateExecution("t-2", "restart_pod").Allowed);
        Assert.False(policy.EvaluateExecution("t-3", "restart_pod").Allowed);
    }

    // ── Case-insensitivity ─────────────────────────────────────────

    [Fact]
    public void Action_type_lookup_is_case_insensitive()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:Restart_Pod:0"] = "t-1"
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Tenant_id_lookup_is_case_insensitive()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"] = "T-1"
        });

        var result = policy.EvaluateExecution("t-1", "restart_pod");

        Assert.True(result.Allowed);
    }

    // ── Multiple action types ──────────────────────────────────────

    [Fact]
    public void Different_action_types_have_independent_allowlists()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"]  = "t-1",
            ["SafeActions:AllowedExecutionTenants:http_probe:0"]   = "t-2"
        });

        Assert.True(policy.EvaluateExecution("t-1", "restart_pod").Allowed);
        Assert.False(policy.EvaluateExecution("t-2", "restart_pod").Allowed);

        Assert.True(policy.EvaluateExecution("t-2", "http_probe").Allowed);
        Assert.False(policy.EvaluateExecution("t-1", "http_probe").Allowed);
    }

    // ── Diagnostic properties ──────────────────────────────────────

    [Fact]
    public void Diagnostic_properties_reflect_config()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["SafeActions:AllowedExecutionTenants:restart_pod:0"]  = "t-1",
            ["SafeActions:AllowedExecutionTenants:restart_pod:1"]  = "t-2",
            ["SafeActions:AllowedExecutionTenants:http_probe:0"]   = "t-3"
        });

        Assert.Equal(2, policy.ConfiguredActionTypeCount);
        Assert.Equal(3, policy.TotalTenantEntryCount);
    }

    [Fact]
    public void Diagnostic_properties_zero_when_empty()
    {
        var policy = CreatePolicy();

        Assert.Equal(0, policy.ConfiguredActionTypeCount);
        Assert.Equal(0, policy.TotalTenantEntryCount);
    }
}
