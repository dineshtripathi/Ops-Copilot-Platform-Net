using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="TenantWorkspaceResolver"/> — four canonical cases.
/// </summary>
public sealed class TenantWorkspaceResolverTests
{
    private const string TenantId   = "tenant-test-40";
    private const string ValidGuid  = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    private const string InvalidVal = "not-a-guid";

    private static TenantWorkspaceResolver BuildResolver(IConfiguration cfg)
        => new(cfg, NullLogger<TenantWorkspaceResolver>.Instance);

    private static TenantWorkspaceResolver BuildResolverWithDiscovery(
        IConfiguration cfg,
        IObservabilityResourceDiscovery discovery)
        => new(cfg, NullLogger<TenantWorkspaceResolver>.Instance, discovery);

    private static IConfiguration BuildConfig(Dictionary<string, string?> data)
        => new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    // ═══════════════════════════════════════════════════════════════
    // 1. Missing workspace key → missing_workspace
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_MissingWorkspaceKey_ReturnsMissingWorkspace()
    {
        var cfg      = BuildConfig(new Dictionary<string, string?>());
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.False(result.Success);
        Assert.Null(result.WorkspaceId);
        Assert.Equal("missing_workspace", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Key present but value is not a valid GUID → missing_workspace
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_InvalidGuid_ReturnsMissingWorkspace()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            [$"Tenants:{TenantId}:Observability:LogAnalyticsWorkspaceId"] = InvalidVal
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.False(result.Success);
        Assert.Null(result.WorkspaceId);
        Assert.Equal("missing_workspace", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Valid GUID, in allowlist (or allowlist empty) → success
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_ValidGuid_InAllowlist_ReturnsSuccess()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            [$"Tenants:{TenantId}:Observability:LogAnalyticsWorkspaceId"] = ValidGuid,
            ["SafeActions:AllowedLogAnalyticsWorkspaceIds:0"]              = ValidGuid
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
        Assert.Null(result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Valid GUID, allowlist non-empty but GUID not in it →
    //    workspace_not_allowlisted
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_ValidGuid_NotInAllowlist_ReturnsNotAllowlisted()
    {
        var otherGuid = "00000000-0000-0000-0000-000000000099";
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            [$"Tenants:{TenantId}:Observability:LogAnalyticsWorkspaceId"] = ValidGuid,
            ["SafeActions:AllowedLogAnalyticsWorkspaceIds:0"]              = otherGuid
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.False(result.Success);
        Assert.Null(result.WorkspaceId);
        Assert.Equal("workspace_not_allowlisted", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. No allowlist configured (empty) → GUID resolves successfully
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_ValidGuid_EmptyAllowlist_ReturnsSuccess()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            [$"Tenants:{TenantId}:Observability:LogAnalyticsWorkspaceId"] = ValidGuid
            // No SafeActions:AllowedLogAnalyticsWorkspaceIds → allowlist absent
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
        Assert.Null(result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Global fallback key used when tenant-specific key absent
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_GlobalFallbackKey_UsedWhenTenantKeyAbsent()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["Observability:LogAnalyticsWorkspaceId"] = ValidGuid
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
    }

    [Fact]
    public void Resolve_LegacyWorkspaceIdFallback_UsedWhenOtherKeysAbsent()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["WORKSPACE_ID"] = ValidGuid
        });
        var resolver = BuildResolver(cfg);

        var result = resolver.Resolve(TenantId);

        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. ResolveAsync auto-discovers single workspace (no config)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_SingleDiscoveredWorkspace_AutoSelectsWithoutConfig()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>());
        var discovery = new StubDiscovery(new[]
        {
            new ObservabilityResourcePair(ValidGuid, string.Empty, string.Empty)
            {
                SubscriptionId = "sub-1",
            },
        });
        var resolver = BuildResolverWithDiscovery(cfg, discovery);

        var result = await resolver.ResolveAsync(TenantId);

        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. ResolveAsync uses Tenants:{tenantId}:SubscriptionId to pick
    //    the correct workspace from a multi-subscription discovery
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_SubscriptionConfig_SelectsMatchingSubscriptionWorkspace()
    {
        const string SubA     = "sub-aaaa";
        const string SubB     = "sub-bbbb";
        const string GuidSubA = "aaaaaaaa-0000-0000-0000-000000000001";
        const string GuidSubB = "bbbbbbbb-0000-0000-0000-000000000002";

        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            [$"Tenants:{TenantId}:SubscriptionId"] = SubB,
        });
        var discovery = new StubDiscovery(new[]
        {
            new ObservabilityResourcePair(GuidSubA, string.Empty, string.Empty) { SubscriptionId = SubA },
            new ObservabilityResourcePair(GuidSubB, string.Empty, string.Empty) { SubscriptionId = SubB },
        });
        var resolver = BuildResolverWithDiscovery(cfg, discovery);

        var result = await resolver.ResolveAsync(TenantId);

        Assert.True(result.Success);
        Assert.Equal(GuidSubB, result.WorkspaceId);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Multiple distinct workspaces, no subscription config →
    //     cannot auto-select; returns base failure result
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_MultipleWorkspaces_NoSubscriptionConfig_DoesNotAutoSelect()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>());
        var discovery = new StubDiscovery(new[]
        {
            new ObservabilityResourcePair("guid-1111-1111-1111-111111111111", string.Empty, string.Empty),
            new ObservabilityResourcePair("guid-2222-2222-2222-222222222222", string.Empty, string.Empty),
        });
        var resolver = BuildResolverWithDiscovery(cfg, discovery);

        var result = await resolver.ResolveAsync(TenantId);

        // No config workspace + ambiguous discovery → overall failure
        Assert.False(result.Success);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Two AI components share one workspace → deduplicated,
    //     auto-select succeeds (single underlying LAW)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_TwoAiComponentsSameWorkspace_DeduplicatesAndAutoSelects()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>());
        const string AiPath1 = "/providers/microsoft.insights/components/ai-1";
        const string AiPath2 = "/providers/microsoft.insights/components/ai-2";

        var discovery = new StubDiscovery(new[]
        {
            new ObservabilityResourcePair(ValidGuid, "ai-1", AiPath1) { SubscriptionId = "sub-x" },
            new ObservabilityResourcePair(ValidGuid, "ai-2", AiPath2) { SubscriptionId = "sub-x" },
        });
        var resolver = BuildResolverWithDiscovery(cfg, discovery);

        var result = await resolver.ResolveAsync(TenantId);

        // Both rows share ValidGuid — deduplication reduces to 1 workspace → auto-select
        Assert.True(result.Success);
        Assert.Equal(ValidGuid, result.WorkspaceId);
    }

    // ── Stub ──────────────────────────────────────────────────────

    private sealed class StubDiscovery(IReadOnlyList<ObservabilityResourcePair> pairs)
        : IObservabilityResourceDiscovery
    {
        public Task<IReadOnlyList<ObservabilityResourcePair>> DiscoverAsync(
            CancellationToken ct = default) => Task.FromResult(pairs);
    }
}
