using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.BuildingBlocks.Contracts.Tenancy;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class TenantAwareGovernanceOptionsResolverTests
{
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static IOptions<GovernanceOptions> WrapOptions(GovernanceOptions opts)
        => Options.Create(opts);

    // ── 1. SQL-backed config takes highest priority ──────────────────

    [Fact]
    public void Resolve_SqlConfigAvailable_ReturnsSqlValues()
    {
        // Arrange
        var sqlConfig = new TenantGovernanceConfig(
            ["sql_tool"], TokenBudget: 5000, SessionTtlMinutes: 15);

        var provider = new Mock<ITenantConfigProvider>();
        provider.Setup(p => p.GetGovernanceConfig(TenantId)).Returns(sqlConfig);

        var opts = new GovernanceOptions();
        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider.Object);

        // Act
        var result = sut.Resolve(TenantId);

        // Assert
        Assert.Equal(["sql_tool"], result.AllowedTools);
        Assert.Equal(5000, result.TokenBudget);
        Assert.Equal(15, result.SessionTtlMinutes);
    }

    // ── 2. Null provider → config-file overrides ──────────────────────

    [Fact]
    public void Resolve_NullProvider_FallsBackToConfigOverride()
    {
        // Arrange
        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["override_tool"],
            TokenBudget = 2000,
            SessionTtlMinutes = 20
        };

        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider: null);

        // Act
        var result = sut.Resolve(TenantId);

        // Assert
        Assert.Equal(["override_tool"], result.AllowedTools);
        Assert.Equal(2000, result.TokenBudget);
        Assert.Equal(20, result.SessionTtlMinutes);
    }

    // ── 3. No override for tenant → falls back to Defaults ───────────

    [Fact]
    public void Resolve_NoOverrideNoSql_ReturnsDefaults()
    {
        var opts = new GovernanceOptions();
        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider: null);

        var result = sut.Resolve(TenantId);

        Assert.Equal(opts.Defaults.AllowedTools, result.AllowedTools);
        Assert.Equal(opts.Defaults.TokenBudget, result.TokenBudget);
        Assert.Equal(opts.Defaults.SessionTtlMinutes, result.SessionTtlMinutes);
    }

    // ── 4. Provider returns null → config override used ──────────────

    [Fact]
    public void Resolve_ProviderReturnsNull_FallsBackToOverride()
    {
        // Arrange
        var provider = new Mock<ITenantConfigProvider>();
        provider.Setup(p => p.GetGovernanceConfig(TenantId)).Returns((TenantGovernanceConfig?)null);

        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["fallback_tool"],
            SessionTtlMinutes = 45
        };

        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider.Object);

        // Act
        var result = sut.Resolve(TenantId);

        // Assert
        Assert.Equal(["fallback_tool"], result.AllowedTools);
        Assert.Equal(45, result.SessionTtlMinutes);
    }

    // ── 5. Provider returns null, no override → Defaults ─────────────

    [Fact]
    public void Resolve_ProviderReturnsNull_NoOverride_ReturnsDefaults()
    {
        var provider = new Mock<ITenantConfigProvider>();
        provider.Setup(p => p.GetGovernanceConfig(TenantId)).Returns((TenantGovernanceConfig?)null);

        var opts = new GovernanceOptions();
        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider.Object);

        var result = sut.Resolve(TenantId);

        Assert.Equal(opts.Defaults.AllowedTools, result.AllowedTools);
        Assert.Null(result.TokenBudget);
        Assert.Equal(30, result.SessionTtlMinutes);
    }

    // ── 6. Provider throws → graceful degradation to override ────────

    [Fact]
    public void Resolve_ProviderThrows_FallsBackToOverride()
    {
        var provider = new Mock<ITenantConfigProvider>();
        provider.Setup(p => p.GetGovernanceConfig(TenantId)).Throws<InvalidOperationException>();

        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            AllowedTools = ["safe_tool"],
            TokenBudget = 1000,
            SessionTtlMinutes = 10
        };

        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider.Object);

        var result = sut.Resolve(TenantId);

        Assert.Equal(["safe_tool"], result.AllowedTools);
        Assert.Equal(1000, result.TokenBudget);
        Assert.Equal(10, result.SessionTtlMinutes);
    }

    // ── 7. Config override partial merge — null fields use Defaults ──

    [Fact]
    public void Resolve_PartialOverride_MergesWithDefaults()
    {
        var opts = new GovernanceOptions();
        opts.TenantOverrides[TenantId] = new TenantOverride
        {
            // Only override TokenBudget — AllowedTools + SessionTtlMinutes stay default
            TokenBudget = 7500
        };

        var sut = new TenantAwareGovernanceOptionsResolver(WrapOptions(opts), provider: null);

        var result = sut.Resolve(TenantId);

        Assert.Equal(opts.Defaults.AllowedTools, result.AllowedTools);
        Assert.Equal(7500, result.TokenBudget);
        Assert.Equal(opts.Defaults.SessionTtlMinutes, result.SessionTtlMinutes);
    }
}
