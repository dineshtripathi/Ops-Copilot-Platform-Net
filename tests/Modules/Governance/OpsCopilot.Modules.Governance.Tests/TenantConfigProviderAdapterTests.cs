using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class TenantConfigProviderAdapterTests
{
    // ── 1. Valid GUID → maps EffectiveTenantConfig to TenantGovernanceConfig ──

    [Fact]
    public void GetGovernanceConfig_ValidGuid_ReturnsMappedConfig()
    {
        var tenantGuid = Guid.NewGuid();
        var effective = new EffectiveTenantConfig(
            AllowedTools: ["kql_query"],
            TriageEnabled: true,
            TokenBudget: 3000,
            SessionTtlMinutes: 20);

        var resolver = new Mock<ITenantConfigResolver>();
        resolver.Setup(r => r.ResolveAsync(tenantGuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(effective);

        var sut = new TenantConfigProviderAdapter(resolver.Object);

        var result = sut.GetGovernanceConfig(tenantGuid.ToString());

        Assert.NotNull(result);
        Assert.Equal(["kql_query"], result!.AllowedTools);
        Assert.Equal(3000, result.TokenBudget);
        Assert.Equal(20, result.SessionTtlMinutes);
    }

    // ── 2. Invalid GUID string → returns null ────────────────────────

    [Fact]
    public void GetGovernanceConfig_InvalidGuid_ReturnsNull()
    {
        var resolver = new Mock<ITenantConfigResolver>();
        var sut = new TenantConfigProviderAdapter(resolver.Object);

        var result = sut.GetGovernanceConfig("not-a-guid");

        Assert.Null(result);
    }
}
