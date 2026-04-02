using OpsCopilot.Rag.Application.Acl;
using OpsCopilot.Rag.Domain.Acl;
using OpsCopilot.Rag.Infrastructure.Acl;
using Xunit;

namespace OpsCopilot.Modules.Rag.Tests;

/// <summary>
/// Tests for §6.17 RAG ACL Service — NullAclFilterService + domain model contracts.
/// </summary>
public sealed class AclFilterServiceTests
{
    // ── NullAclFilterService ─────────────────────────────────────────────

    [Fact]
    public async Task NullAclFilterService_ReturnsEmptyFilterExpression()
    {
        var sut = new NullAclFilterService();
        var result = await sut.GetAclFilterAsync("tenant-1", [], "production");
        Assert.Equal(string.Empty, result.FilterExpression);
    }

    [Fact]
    public async Task NullAclFilterService_ReturnsEmptyPolicyId()
    {
        var sut = new NullAclFilterService();
        var result = await sut.GetAclFilterAsync("tenant-1", [], "production");
        Assert.Equal(string.Empty, result.AclPolicyId);
    }

    [Fact]
    public async Task NullAclFilterService_SameResultForAnyTenant()
    {
        var sut = new NullAclFilterService();
        var r1 = await sut.GetAclFilterAsync("tenant-A", [], "production");
        var r2 = await sut.GetAclFilterAsync("tenant-B", [], "production");
        Assert.Equal(r1, r2);
    }

    [Fact]
    public async Task NullAclFilterService_SameResultForAnyEnvironment()
    {
        var sut = new NullAclFilterService();
        var r1 = await sut.GetAclFilterAsync("t1", [], "production");
        var r2 = await sut.GetAclFilterAsync("t1", [], "staging");
        Assert.Equal(r1, r2);
    }

    [Fact]
    public async Task NullAclFilterService_SameResultForAnyUserClaims()
    {
        var sut = new NullAclFilterService();
        var r1 = await sut.GetAclFilterAsync("t1", ["role:admin", "team:ops"], "production");
        var r2 = await sut.GetAclFilterAsync("t1", [], "production");
        Assert.Equal(r1, r2);
    }

    // ── IAclFilterService contract ────────────────────────────────────────

    [Fact]
    public void NullAclFilterService_ImplementsInterface()
    {
        IAclFilterService svc = new NullAclFilterService();
        Assert.NotNull(svc);
    }

    // ── AclFilter record contracts ────────────────────────────────────────

    [Fact]
    public void AclFilter_StoresFilterExpression()
    {
        var filter = new AclFilter(FilterExpression: "tenantId eq 'abc'", AclPolicyId: "p1");
        Assert.Equal("tenantId eq 'abc'", filter.FilterExpression);
    }

    [Fact]
    public void AclFilter_StoresAclPolicyId()
    {
        var filter = new AclFilter(FilterExpression: "tenantId eq 'abc'", AclPolicyId: "p1");
        Assert.Equal("p1", filter.AclPolicyId);
    }

    // ── AclPolicy model contracts ─────────────────────────────────────────

    [Fact]
    public void AclPolicy_AllowedEnvironmentsDefaultsEmpty()
    {
        var policy = new AclPolicy { PolicyId = "p1", TenantId = "t1" };
        Assert.Empty(policy.AllowedEnvironments);
    }

    [Fact]
    public void AclPolicy_RequiredClaimsDefaultsEmpty()
    {
        var policy = new AclPolicy { PolicyId = "p1", TenantId = "t1" };
        Assert.Empty(policy.RequiredClaims);
    }

    [Fact]
    public void AclPolicy_StoresPolicyIdAndTenantId()
    {
        var policy = new AclPolicy { PolicyId = "policy-42", TenantId = "tenant-99" };
        Assert.Equal("policy-42", policy.PolicyId);
        Assert.Equal("tenant-99", policy.TenantId);
    }
}

/// <summary>
/// Tests for §6.17 Tenant-Scoped RAG ACL — TenantGroupRoleAclFilterService.
/// Slice 179.
/// </summary>
public sealed class TenantGroupRoleAclFilterServiceTests
{
    [Fact]
    public async Task FilterExpression_ContainsTenantId()
    {
        var sut    = new TenantGroupRoleAclFilterService();
        var result = await sut.GetAclFilterAsync("my-tenant", [], "production");
        Assert.Contains("my-tenant", result.FilterExpression);
    }

    [Fact]
    public async Task AclPolicyId_IsTenantGroupRole()
    {
        var sut    = new TenantGroupRoleAclFilterService();
        var result = await sut.GetAclFilterAsync("t1", [], "production");
        Assert.Equal(TenantGroupRoleAclFilterService.PolicyId, result.AclPolicyId);
    }

    [Fact]
    public async Task DifferentTenants_ReturnDifferentFilterExpressions()
    {
        var sut = new TenantGroupRoleAclFilterService();
        var r1  = await sut.GetAclFilterAsync("tenant-A", [], "production");
        var r2  = await sut.GetAclFilterAsync("tenant-B", [], "production");
        Assert.NotEqual(r1.FilterExpression, r2.FilterExpression);
    }

    [Fact]
    public void TenantGroupRoleAclFilterService_ImplementsInterface()
    {
        IAclFilterService svc = new TenantGroupRoleAclFilterService();
        Assert.NotNull(svc);
    }
}

/// <summary>
/// Tests for §6.18 Entra-Group-Aware RAG ACL — EntraGroupAclFilterService.
/// Slice 191.
/// </summary>
public sealed class EntraGroupAclFilterServiceTests
{
    [Fact]
    public async Task NoClaims_ReturnsOnlyTenantFilter()
    {
        var sut    = new EntraGroupAclFilterService();
        var result = await sut.GetAclFilterAsync("t1", [], "production");
        Assert.Equal("tenantId eq 't1'", result.FilterExpression);
    }

    [Fact]
    public async Task WithClaims_FilterContainsTenantId()
    {
        var sut    = new EntraGroupAclFilterService();
        var result = await sut.GetAclFilterAsync("tenant-X", ["gid-1", "gid-2"], "production");
        Assert.Contains("tenant-X", result.FilterExpression);
    }

    [Fact]
    public async Task WithClaims_FilterContainsGroupPrefix()
    {
        var sut    = new EntraGroupAclFilterService();
        var result = await sut.GetAclFilterAsync("t1", ["abc-123"], "production");
        Assert.Contains("group:abc-123", result.FilterExpression);
    }

    [Fact]
    public async Task WithClaims_FilterContainsAllTenantsTag()
    {
        var sut    = new EntraGroupAclFilterService();
        var result = await sut.GetAclFilterAsync("t1", ["gid-1"], "production");
        Assert.Contains(EntraGroupAclFilterService.AllTenantsTag, result.FilterExpression);
    }

    [Fact]
    public async Task AclPolicyId_IsEntraGroupRole()
    {
        var sut    = new EntraGroupAclFilterService();
        var result = await sut.GetAclFilterAsync("t1", [], "production");
        Assert.Equal(EntraGroupAclFilterService.PolicyId, result.AclPolicyId);
    }

    [Fact]
    public async Task DifferentTenants_ReturnDifferentFilterExpressions()
    {
        var sut = new EntraGroupAclFilterService();
        var r1  = await sut.GetAclFilterAsync("tenant-A", ["gid-1"], "production");
        var r2  = await sut.GetAclFilterAsync("tenant-B", ["gid-1"], "production");
        Assert.NotEqual(r1.FilterExpression, r2.FilterExpression);
    }

    [Fact]
    public void EntraGroupAclFilterService_ImplementsInterface()
    {
        IAclFilterService svc = new EntraGroupAclFilterService();
        Assert.NotNull(svc);
    }
}
