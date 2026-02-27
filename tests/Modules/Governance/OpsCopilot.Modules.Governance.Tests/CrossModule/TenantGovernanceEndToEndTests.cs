using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests.CrossModule;

/// <summary>
/// Slice 30 — Cross-module integration tests proving that tenant config
/// stored in the Tenancy SQL layer drives Governance policy decisions
/// at runtime through the full resolution chain:
///
///   ITenantConfigResolver (Tenancy)
///     → TenantConfigProviderAdapter (bridge)
///       → TenantAwareGovernanceOptionsResolver (3-tier)
///         → DefaultToolAllowlistPolicy / DefaultTokenBudgetPolicy / DefaultSessionPolicy
///
/// Each test wires up REAL production classes — only the async
/// <see cref="ITenantConfigResolver"/> is mocked (simulating the DB layer).
/// </summary>
public sealed class TenantGovernanceEndToEndTests
{
    private static readonly Guid TenantGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static IOptions<GovernanceOptions> DefaultOptions() =>
        Options.Create(new GovernanceOptions());

    /// <summary>
    /// Builds the full cross-module chain:
    ///   Mock ITenantConfigResolver → real TenantConfigProviderAdapter
    ///     → real TenantAwareGovernanceOptionsResolver
    /// </summary>
    private static (
        TenantAwareGovernanceOptionsResolver resolver,
        Mock<ITenantConfigResolver> sqlMock)
        BuildChain(
            EffectiveTenantConfig? sqlResult,
            GovernanceOptions? options = null,
            bool sqlThrows = false)
    {
        var sqlMock = new Mock<ITenantConfigResolver>();

        if (sqlThrows)
        {
            sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SQL unavailable"));
        }
        else if (sqlResult is not null)
        {
            sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sqlResult);
        }
        else
        {
            // Simulate "no config rows" — adapter catches and returns null
            sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new KeyNotFoundException("Tenant not found"));
        }

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var opts = options ?? new GovernanceOptions();
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(opts), adapter);

        return (resolver, sqlMock);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1. SQL-backed AllowedTools → Allowlist policy enforces restriction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlAllowedTools_AllowlistPolicyEnforces()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (resolver, _) = BuildChain(sqlConfig);
        var policy = new DefaultToolAllowlistPolicy(resolver);

        // kql_query is in SQL allowlist → allowed
        Assert.True(policy.CanUseTool(TenantId, "kql_query").Allowed);

        // runbook_search is NOT in SQL allowlist → denied
        var denied = policy.CanUseTool(TenantId, "runbook_search");
        Assert.False(denied.Allowed);
        Assert.Equal("TOOL_DENIED", denied.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. SQL-backed TokenBudget → Budget policy returns capped budget
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlTokenBudget_BudgetPolicyReturnsCap()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query", "runbook_search"],
            TriageEnabled: true,
            TokenBudget: 2048,
            SessionTtlMinutes: 30);

        var (resolver, _) = BuildChain(sqlConfig);
        var policy = new DefaultTokenBudgetPolicy(resolver);

        var result = policy.CheckRunBudget(TenantId, Guid.NewGuid());

        Assert.True(result.Allowed);
        Assert.Equal(2048, result.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. SQL-backed SessionTtlMinutes → Session policy returns TTL
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlSessionTtl_SessionPolicyReturnsTtl()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: [],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 15);

        var (resolver, _) = BuildChain(sqlConfig);
        var policy = new DefaultSessionPolicy(resolver);

        var result = policy.GetSessionTtl(TenantId);

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. SQL unavailable → graceful degradation to config-file override
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlUnavailable_FallsBackToConfigOverride()
    {
        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["override_tool"],
            TokenBudget = 1000,
            SessionTtlMinutes = 20
        };

        var (resolver, _) = BuildChain(
            sqlResult: null, options: opts, sqlThrows: true);
        var policy = new DefaultToolAllowlistPolicy(resolver);

        // override_tool allowed from config-file override
        Assert.True(policy.CanUseTool(TenantId, "override_tool").Allowed);

        // anything else denied
        Assert.False(policy.CanUseTool(TenantId, "kql_query").Allowed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. SQL unavailable, no config override → falls to Defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlUnavailableNoOverride_FallsToDefaults()
    {
        var (resolver, _) = BuildChain(
            sqlResult: null, sqlThrows: true);

        var policy = new DefaultToolAllowlistPolicy(resolver);

        // Defaults: AllowedTools = ["kql_query", "runbook_search"]
        Assert.True(policy.CanUseTool(TenantId, "kql_query").Allowed);
        Assert.True(policy.CanUseTool(TenantId, "runbook_search").Allowed);
        Assert.False(policy.CanUseTool(TenantId, "dangerous_tool").Allowed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 6. Two tenants get different resolved governance simultaneously
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_TwoTenants_DifferentGovernance()
    {
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var sqlMock = new Mock<ITenantConfigResolver>();

        // Tenant A: only kql_query allowed
        sqlMock.Setup(r => r.ResolveAsync(tenantA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                ["kql_query"], TriageEnabled: true, TokenBudget: 1000, SessionTtlMinutes: 10));

        // Tenant B: only runbook_search allowed
        sqlMock.Setup(r => r.ResolveAsync(tenantB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                ["runbook_search"], TriageEnabled: true, TokenBudget: 5000, SessionTtlMinutes: 60));

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            DefaultOptions(), adapter);

        var allowlistPolicy = new DefaultToolAllowlistPolicy(resolver);
        var budgetPolicy = new DefaultTokenBudgetPolicy(resolver);
        var sessionPolicy = new DefaultSessionPolicy(resolver);

        // Tenant A assertions
        Assert.True(allowlistPolicy.CanUseTool(tenantA.ToString(), "kql_query").Allowed);
        Assert.False(allowlistPolicy.CanUseTool(tenantA.ToString(), "runbook_search").Allowed);
        Assert.Equal(1000, budgetPolicy.CheckRunBudget(tenantA.ToString(), Guid.NewGuid()).MaxTokens);
        Assert.Equal(TimeSpan.FromMinutes(10), sessionPolicy.GetSessionTtl(tenantA.ToString()));

        // Tenant B assertions
        Assert.False(allowlistPolicy.CanUseTool(tenantB.ToString(), "kql_query").Allowed);
        Assert.True(allowlistPolicy.CanUseTool(tenantB.ToString(), "runbook_search").Allowed);
        Assert.Equal(5000, budgetPolicy.CheckRunBudget(tenantB.ToString(), Guid.NewGuid()).MaxTokens);
        Assert.Equal(TimeSpan.FromMinutes(60), sessionPolicy.GetSessionTtl(tenantB.ToString()));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 7. Invalid tenant ID (non-GUID) → adapter returns null → Defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_InvalidTenantId_FallsBackToDefaults()
    {
        var sqlMock = new Mock<ITenantConfigResolver>();
        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            DefaultOptions(), adapter);

        var policy = new DefaultToolAllowlistPolicy(resolver);

        // "not-a-guid" → Guid.TryParse fails → adapter returns null → Defaults
        Assert.True(policy.CanUseTool("not-a-guid", "kql_query").Allowed);
        Assert.True(policy.CanUseTool("not-a-guid", "runbook_search").Allowed);

        // SQL resolver should never be called for invalid GUID
        sqlMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 8. SQL overrides only AllowedTools — TokenBudget+SessionTtl
    //    come from SQL too (EffectiveTenantConfig merges at Tenancy layer)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlReturnsFullConfig_AllFieldsHonored()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["custom_tool_alpha", "custom_tool_beta"],
            TriageEnabled: false,
            TokenBudget: 9999,
            SessionTtlMinutes: 120);

        var (resolver, _) = BuildChain(sqlConfig);

        var allowPolicy = new DefaultToolAllowlistPolicy(resolver);
        var budgetPolicy = new DefaultTokenBudgetPolicy(resolver);
        var sessionPolicy = new DefaultSessionPolicy(resolver);

        Assert.True(allowPolicy.CanUseTool(TenantId, "custom_tool_alpha").Allowed);
        Assert.True(allowPolicy.CanUseTool(TenantId, "custom_tool_beta").Allowed);
        Assert.False(allowPolicy.CanUseTool(TenantId, "kql_query").Allowed);
        Assert.Equal(9999, budgetPolicy.CheckRunBudget(TenantId, Guid.NewGuid()).MaxTokens);
        Assert.Equal(TimeSpan.FromMinutes(120), sessionPolicy.GetSessionTtl(TenantId));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 9. SQL null budget → unlimited (null MaxTokens)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlNullBudget_BudgetPolicyReturnsUnlimited()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (resolver, _) = BuildChain(sqlConfig);
        var policy = new DefaultTokenBudgetPolicy(resolver);

        var result = policy.CheckRunBudget(TenantId, Guid.NewGuid());

        Assert.True(result.Allowed);
        Assert.Null(result.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 10. Empty SQL allowlist → all tools allowed (open-gate)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlEmptyAllowlist_AllToolsAllowed()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: [],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (resolver, _) = BuildChain(sqlConfig);
        var policy = new DefaultToolAllowlistPolicy(resolver);

        Assert.True(policy.CanUseTool(TenantId, "any_tool_x").Allowed);
        Assert.True(policy.CanUseTool(TenantId, "any_tool_y").Allowed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 11. Adapter maps EffectiveTenantConfig (4 fields) →
    //     TenantGovernanceConfig (3 fields, drops TriageEnabled)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Adapter_DropsTriageEnabled_MapsRemainingFields()
    {
        var sqlMock = new Mock<ITenantConfigResolver>();
        sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: ["tool_a", "tool_b"],
                TriageEnabled: false,   // dropped by adapter
                TokenBudget: 4096,
                SessionTtlMinutes: 45));

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var govConfig = adapter.GetGovernanceConfig(TenantId);

        Assert.NotNull(govConfig);
        Assert.Equal(["tool_a", "tool_b"], govConfig!.AllowedTools);
        Assert.Equal(4096, govConfig.TokenBudget);
        Assert.Equal(45, govConfig.SessionTtlMinutes);
        // TriageEnabled is not part of TenantGovernanceConfig — by design
    }

    // ═══════════════════════════════════════════════════════════════════
    // 12. SQL priority trumps config-file override (P1 > P2)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullChain_SqlOverridesConfigFile_SqlWins()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["sql_only_tool"],
            TriageEnabled: true,
            TokenBudget: 500,
            SessionTtlMinutes: 5);

        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["config_override_tool"],
            TokenBudget = 9999,
            SessionTtlMinutes = 99
        };

        var (resolver, _) = BuildChain(sqlConfig, opts);

        var allowPolicy = new DefaultToolAllowlistPolicy(resolver);
        var budgetPolicy = new DefaultTokenBudgetPolicy(resolver);
        var sessionPolicy = new DefaultSessionPolicy(resolver);

        // SQL values win over config-file overrides
        Assert.True(allowPolicy.CanUseTool(TenantId, "sql_only_tool").Allowed);
        Assert.False(allowPolicy.CanUseTool(TenantId, "config_override_tool").Allowed);
        Assert.Equal(500, budgetPolicy.CheckRunBudget(TenantId, Guid.NewGuid()).MaxTokens);
        Assert.Equal(TimeSpan.FromMinutes(5), sessionPolicy.GetSessionTtl(TenantId));
    }
}
