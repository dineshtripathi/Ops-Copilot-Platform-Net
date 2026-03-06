using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
}
