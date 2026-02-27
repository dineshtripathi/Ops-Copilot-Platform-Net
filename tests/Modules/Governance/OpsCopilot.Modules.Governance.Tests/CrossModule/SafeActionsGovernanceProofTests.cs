using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests.CrossModule;

/// <summary>
/// Slice 30 — SafeActions proof tests.
///
/// Proves that the shared <see cref="PolicyDecision"/> contract produced by
/// the full Governance resolution chain (SQL → adapter → resolver → policy)
/// correctly drives allow/deny outcomes that SafeActions would consume
/// through <c>ISafeActionPolicy.Evaluate</c>.
///
/// Both SafeActions (<c>ISafeActionPolicy</c>) and AgentRuns
/// (<c>IToolAllowlistPolicy</c>) share the same <see cref="PolicyDecision"/>
/// record type, so a denial from Governance produces the identical shape
/// that triggers <c>PolicyDeniedException</c> in the SafeActions orchestrator.
/// </summary>
public sealed class SafeActionsGovernanceProofTests
{
    private static readonly Guid TenantGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    /// <summary>
    /// Builds the full cross-module chain with a SQL-simulated tenant config.
    /// </summary>
    private static DefaultToolAllowlistPolicy BuildAllowlistPolicy(
        EffectiveTenantConfig sqlConfig)
    {
        var sqlMock = new Mock<ITenantConfigResolver>();
        sqlMock.Setup(r => r.ResolveAsync(TenantGuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sqlConfig);

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(new GovernanceOptions()), adapter);

        return new DefaultToolAllowlistPolicy(resolver);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1. Restricted tenant — governance chain produces Deny decision
    //    SafeActions would throw PolicyDeniedException for this decision
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SafeActionsScenario_RestrictedTenant_GovernanceDeniesUnlisted()
    {
        // Tenant SQL config allows only "kql_query"
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: null,
            SessionTtlMinutes: 30);

        var policy = BuildAllowlistPolicy(sqlConfig);

        // SafeActions would call CanUseTool before proposing — simulate with "restart_vm"
        PolicyDecision decision = policy.CanUseTool(TenantId, "restart_vm");

        // This is exactly the shape that triggers PolicyDeniedException in
        // SafeActionOrchestrator.ProposeAsync → Deny + TOOL_DENIED
        Assert.False(decision.Allowed);
        Assert.Equal("TOOL_DENIED", decision.ReasonCode);
        Assert.Contains("restart_vm", decision.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. Permissive tenant — governance chain produces Allow decision
    //    SafeActions would proceed with the action for this decision
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SafeActionsScenario_PermissiveTenant_GovernanceAllowsListed()
    {
        // Tenant SQL config allows both tools
        var sqlConfig = new EffectiveTenantConfig(
            AllowedTools: ["kql_query", "restart_vm"],
            TriageEnabled: true,
            TokenBudget: 5000,
            SessionTtlMinutes: 60);

        var policy = BuildAllowlistPolicy(sqlConfig);

        // Same action type that was denied above — now allowed for this tenant
        PolicyDecision decision = policy.CanUseTool(TenantId, "restart_vm");

        // SafeActions would proceed — no PolicyDeniedException thrown
        Assert.True(decision.Allowed);
        Assert.Equal("ALLOWED", decision.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. Two tenants, same action — one allowed, one denied
    //    Proves per-tenant governance isolation in SafeActions context
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SafeActionsScenario_TwoTenants_DifferentDecisions()
    {
        var tenantRestricted = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantPermissive = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var sqlMock = new Mock<ITenantConfigResolver>();
        sqlMock.Setup(r => r.ResolveAsync(tenantRestricted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: ["kql_query"],  // no restart_vm
                TriageEnabled: true, TokenBudget: null, SessionTtlMinutes: 30));

        sqlMock.Setup(r => r.ResolveAsync(tenantPermissive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: ["kql_query", "restart_vm"],
                TriageEnabled: true, TokenBudget: null, SessionTtlMinutes: 30));

        var adapter = new TenantConfigProviderAdapter(sqlMock.Object);
        var resolver = new TenantAwareGovernanceOptionsResolver(
            Options.Create(new GovernanceOptions()), adapter);
        var policy = new DefaultToolAllowlistPolicy(resolver);

        // Same tool, different tenants
        var restrictedDecision = policy.CanUseTool(tenantRestricted.ToString(), "restart_vm");
        var permissiveDecision = policy.CanUseTool(tenantPermissive.ToString(), "restart_vm");

        // Restricted tenant → denied (SafeActions would throw PolicyDeniedException)
        Assert.False(restrictedDecision.Allowed);
        Assert.Equal("TOOL_DENIED", restrictedDecision.ReasonCode);

        // Permissive tenant → allowed (SafeActions would proceed)
        Assert.True(permissiveDecision.Allowed);
    }
}
