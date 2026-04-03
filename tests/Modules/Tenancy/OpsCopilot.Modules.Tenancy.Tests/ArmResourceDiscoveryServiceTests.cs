using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Infrastructure.Persistence;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

public sealed class ArmResourceDiscoveryServiceTests : IDisposable
{
    private readonly TenancyDbContext _db;
    private readonly ArmResourceDiscoveryService _sut;

    public ArmResourceDiscoveryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new TenancyDbContext(options);
        _sut = new ArmResourceDiscoveryService(_db, NullLogger<ArmResourceDiscoveryService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task DiscoverAsync_NoEntries_ReturnsZeroCountAndEmptyConnectors()
    {
        var tenantId = Guid.NewGuid();

        var result = await _sut.DiscoverAsync(tenantId);

        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(0, result.DiscoveredResourceCount);
        Assert.Empty(result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_WithAppInsightsKey_DetectsAppInsightsConnector()
    {
        var tenantId = Guid.NewGuid();
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "AppInsightsWorkspaceId", "ws-001"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "TriageEnabled", "true"));
        await _db.SaveChangesAsync();

        var result = await _sut.DiscoverAsync(tenantId);

        Assert.Equal(2, result.DiscoveredResourceCount);
        Assert.Contains("app-insights", result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_TwoAppInsightsKeys_DeduplicatesConnector()
    {
        var tenantId = Guid.NewGuid();
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "AppInsightsConnectionString", "cs-a"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "AppInsightsWorkspaceId",     "ws-b"));
        await _db.SaveChangesAsync();

        var result = await _sut.DiscoverAsync(tenantId);

        Assert.Equal(2, result.DiscoveredResourceCount);
        Assert.Single(result.DetectedConnectors);
        Assert.Contains("app-insights", result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_MultipleKnownKeys_DetectsAllConnectors()
    {
        var tenantId = Guid.NewGuid();
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "AppInsightsKey",       "ai"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "LogAnalyticsWorkspace", "la"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, "SubscriptionId",        "sub-1"));
        await _db.SaveChangesAsync();

        var result = await _sut.DiscoverAsync(tenantId);

        Assert.Equal(3, result.DiscoveredResourceCount);
        Assert.Contains("app-insights", result.DetectedConnectors);
        Assert.Contains("log-analytics", result.DetectedConnectors);
        Assert.Contains("arm", result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_OnlyCountsEntriesForRequestedTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantA, "AppInsightsKey", "ai-a"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantB, "LogAnalytics",   "la-b"));
        _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantB, "TriageEnabled",  "true"));
        await _db.SaveChangesAsync();

        var resultA = await _sut.DiscoverAsync(tenantA);
        var resultB = await _sut.DiscoverAsync(tenantB);

        Assert.Equal(1, resultA.DiscoveredResourceCount);
        Assert.Equal(2, resultB.DiscoveredResourceCount);
    }
}
