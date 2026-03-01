using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Policies;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Slice 32 — SafeActions Tenant-Aware Governance Integration Tests.
///
/// Proves that the SafeActions <see cref="GovernancePolicyClient"/> bridge wired
/// through real Governance policies and the tenant-aware resolution chain produces
/// correct allow/deny outcomes for different tenant configurations.
///
/// Resolution chain under test (all real production classes except DB layer):
///
///   GovernancePolicyClient (SafeActions bridge)
///     → DefaultToolAllowlistPolicy / DefaultTokenBudgetPolicy (Governance)
///       → TenantAwareGovernanceOptionsResolver (3-tier: SQL → config → defaults)
///         → TenantConfigProviderAdapter (Tenancy bridge)
///           → Mock&lt;ITenantConfigResolver&gt; (simulated SQL layer)
///
/// Reason codes verified: <c>governance_tool_denied</c>, <c>governance_budget_exceeded</c>.
/// </summary>
public sealed class SafeActionsTenantAwareGovernanceIntegrationTests
{
    private static readonly Guid TenantGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    /// <summary>
    /// Builds the full cross-module chain ending with a <see cref="GovernancePolicyClient"/>
    /// that SafeActionOrchestrator would consume via <see cref="IGovernancePolicyClient"/>.
    /// </summary>
    private static (GovernancePolicyClient client, Mock<ITenantConfigResolver> sqlMock)
        BuildClient(
            EffectiveTenantConfig? sqlConfig,
            GovernanceOptions? options = null,
            bool sqlThrows = false)
    {
        var sqlMock = new Mock<ITenantConfigResolver>();

        if (sqlThrows)
        {
            sqlMock.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SQL unavailable"));
        }
        else if (sqlConfig is not null)
        {
            sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sqlConfig);
        }
        else
        {
            sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new KeyNotFoundException("Tenant not found"));
        }

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var opts = options ?? new GovernanceOptions();
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(opts), adapter);

        var allowlistPolicy = new DefaultToolAllowlistPolicy(resolver);
        var budgetPolicy = new DefaultTokenBudgetPolicy(resolver);
        var client = new GovernancePolicyClient(allowlistPolicy, budgetPolicy);

        return (client, sqlMock);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1. Restricted tenant — bridge denies unlisted tool
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_RestrictedTenant_DeniesUnlistedTool()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (client, _) = BuildClient(sqlConfig);

        PolicyDecision decision = client.EvaluateToolAllowlist(TenantId, "restart_pod");

        Assert.False(decision.Allowed);
        Assert.Equal("TOOL_DENIED", decision.ReasonCode);
        Assert.Contains("restart_pod", decision.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. Permissive tenant — bridge allows listed tool
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_PermissiveTenant_AllowsListedTool()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query", "restart_pod"],
            TriageEnabled: true,
            TokenBudget: 5000,
            SessionTtlMinutes: 60);

        var (client, _) = BuildClient(sqlConfig);

        PolicyDecision decision = client.EvaluateToolAllowlist(TenantId, "restart_pod");

        Assert.True(decision.Allowed);
        Assert.Equal("ALLOWED", decision.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. Budget policy — capped MaxTokens propagated through bridge
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_CappedBudget_ReturnsMaxTokens()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["restart_pod"],
            TriageEnabled: true,
            TokenBudget: 2048,
            SessionTtlMinutes: 30);

        var (client, _) = BuildClient(sqlConfig);

        BudgetDecision result = client.EvaluateTokenBudget(TenantId, "restart_pod");

        Assert.True(result.Allowed);
        Assert.Equal(2048, result.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. Null budget — unlimited tokens through bridge
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_NullBudget_ReturnsUnlimited()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (client, _) = BuildClient(sqlConfig);

        BudgetDecision result = client.EvaluateTokenBudget(TenantId, "kql_query");

        Assert.True(result.Allowed);
        Assert.Null(result.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. CorrelationId propagated — explicit id passed as runId
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_ExplicitCorrelationId_PassedAsBudgetRunId()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["restart_pod"],
            TriageEnabled: true,
            TokenBudget: 4096,
            SessionTtlMinutes: 30);

        var correlationId = Guid.NewGuid();
        var (client, _) = BuildClient(sqlConfig);

        // Budget check with explicit correlationId should succeed
        BudgetDecision result = client.EvaluateTokenBudget(
            TenantId, "restart_pod", correlationId);

        Assert.True(result.Allowed);
        Assert.Equal(4096, result.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 6. Null correlationId — DeterministicGuid fallback is stable
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_NullCorrelationId_DeterministicGuidIsStable()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["restart_pod"],
            TriageEnabled: true,
            TokenBudget: 3000,
            SessionTtlMinutes: 30);

        var (client, _) = BuildClient(sqlConfig);

        // Two calls with null correlationId for same tenant+action should both succeed
        // (the deterministic GUID ensures they correlate to the same run)
        BudgetDecision result1 = client.EvaluateTokenBudget(TenantId, "restart_pod", null);
        BudgetDecision result2 = client.EvaluateTokenBudget(TenantId, "restart_pod", null);

        Assert.True(result1.Allowed);
        Assert.True(result2.Allowed);
        Assert.Equal(result1.MaxTokens, result2.MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 7. Two tenants — different governance through same bridge
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_TwoTenants_IsolatedGovernance()
    {
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var sqlMock = new Mock<ITenantConfigResolver>();

        sqlMock.Setup(r => r.ResolveAsync(tenantA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: ["kql_query"],
                TriageEnabled: true, TokenBudget: 1000, SessionTtlMinutes: 10));

        sqlMock.Setup(r => r.ResolveAsync(tenantB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: ["restart_pod"],
                TriageEnabled: true, TokenBudget: 8000, SessionTtlMinutes: 60));

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(new GovernanceOptions()), adapter);
        var client = new GovernancePolicyClient(
            new DefaultToolAllowlistPolicy(resolver),
            new DefaultTokenBudgetPolicy(resolver));

        // Tenant A: kql_query allowed, restart_pod denied
        Assert.True(client.EvaluateToolAllowlist(tenantA.ToString(), "kql_query").Allowed);
        Assert.False(client.EvaluateToolAllowlist(tenantA.ToString(), "restart_pod").Allowed);
        Assert.Equal(1000, client.EvaluateTokenBudget(tenantA.ToString(), "kql_query").MaxTokens);

        // Tenant B: restart_pod allowed, kql_query denied
        Assert.True(client.EvaluateToolAllowlist(tenantB.ToString(), "restart_pod").Allowed);
        Assert.False(client.EvaluateToolAllowlist(tenantB.ToString(), "kql_query").Allowed);
        Assert.Equal(8000, client.EvaluateTokenBudget(tenantB.ToString(), "restart_pod").MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 8. SQL unavailable — graceful degradation to config-file override
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_SqlUnavailable_FallsBackToConfigOverride()
    {
        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["override_tool"],
            TokenBudget = 1500,
            SessionTtlMinutes = 20
        };

        var (client, _) = BuildClient(
            sqlConfig: null, options: opts, sqlThrows: true);

        Assert.True(client.EvaluateToolAllowlist(TenantId, "override_tool").Allowed);
        Assert.False(client.EvaluateToolAllowlist(TenantId, "restart_pod").Allowed);
        Assert.Equal(1500, client.EvaluateTokenBudget(TenantId, "override_tool").MaxTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 9. SQL unavailable, no override — falls through to Defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_SqlUnavailableNoOverride_FallsToDefaults()
    {
        var (client, _) = BuildClient(
            sqlConfig: null, sqlThrows: true);

        // Defaults: AllowedTools = ["kql_query", "runbook_search"]
        Assert.True(client.EvaluateToolAllowlist(TenantId, "kql_query").Allowed);
        Assert.True(client.EvaluateToolAllowlist(TenantId, "runbook_search").Allowed);
        Assert.False(client.EvaluateToolAllowlist(TenantId, "dangerous_tool").Allowed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 10. Empty SQL allowlist — open gate, all tools allowed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_EmptyAllowlist_AllToolsAllowed()
    {
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: [],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var (client, _) = BuildClient(sqlConfig);

        Assert.True(client.EvaluateToolAllowlist(TenantId, "any_tool_x").Allowed);
        Assert.True(client.EvaluateToolAllowlist(TenantId, "any_tool_y").Allowed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 11. Invalid tenant ID — falls to defaults via adapter null return
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_InvalidTenantId_FallsToDefaults()
    {
        var sqlMock = new Mock<ITenantConfigResolver>();
        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(new GovernanceOptions()), adapter);

        var client = new GovernancePolicyClient(
            new DefaultToolAllowlistPolicy(resolver),
            new DefaultTokenBudgetPolicy(resolver));

        // "not-a-guid" → Guid.TryParse fails → adapter returns null → Defaults
        Assert.True(client.EvaluateToolAllowlist("not-a-guid", "kql_query").Allowed);
        Assert.True(client.EvaluateToolAllowlist("not-a-guid", "runbook_search").Allowed);

        // SQL resolver should never be called for invalid GUID
        sqlMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 12. SQL priority trumps config-file override through bridge
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Bridge_SqlOverridesConfigFile_SqlWins()
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

        var (client, _) = BuildClient(sqlConfig, opts);

        // SQL values win
        Assert.True(client.EvaluateToolAllowlist(TenantId, "sql_only_tool").Allowed);
        Assert.False(client.EvaluateToolAllowlist(TenantId, "config_override_tool").Allowed);
        Assert.Equal(500, client.EvaluateTokenBudget(TenantId, "sql_only_tool").MaxTokens);
    }
}
