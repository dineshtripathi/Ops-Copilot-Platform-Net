using Microsoft.EntityFrameworkCore;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;
using OpsCopilot.Reporting.Infrastructure.Queries;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// Slice 193 — unit tests for SqlOperationalDashboardQueryService.
/// Uses EF Core InMemory to exercise real LINQ projection logic without SQL Server.
/// </summary>
public sealed class SqlOperationalDashboardQueryServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static ReportingReadDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ReportingReadDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ReportingReadDbContext(options);
    }

    private static AgentRunReadModel MakeRun(
        Guid? id = null,
        string tenantId = "tenant-a",
        string status = "Completed",
        DateTimeOffset? created = null,
        DateTimeOffset? completed = null,
        string? alertSourceType = null,
        decimal? estimatedCost = null)
    {
        return new AgentRunReadModel
        {
            RunId           = id ?? Guid.NewGuid(),
            TenantId        = tenantId,
            Status          = status,
            CreatedAtUtc    = created  ?? new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero),
            CompletedAtUtc  = completed,
            AlertSourceType = alertSourceType,
            EstimatedCost   = estimatedCost,
        };
    }

    // ── GetMttrTrendAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMttrTrend_NoRuns_ReturnsEmpty()
    {
        await using var db  = CreateDb(nameof(GetMttrTrend_NoRuns_ReturnsEmpty));
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetMttrTrendAsync(null, null, null, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMttrTrend_RunWithNoCompletedAt_Excluded()
    {
        await using var db = CreateDb(nameof(GetMttrTrend_RunWithNoCompletedAt_Excluded));
        db.AgentRunRecords.Add(MakeRun(completed: null, alertSourceType: "Disk"));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetMttrTrendAsync(null, null, null, CancellationToken.None);

        // No completed runs → no MTTR data
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMttrTrend_SingleCompletedRun_ReturnsTrendPoint()
    {
        await using var db = CreateDb(nameof(GetMttrTrend_SingleCompletedRun_ReturnsTrendPoint));
        var created   = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var completed = created.AddMinutes(30);
        db.AgentRunRecords.Add(MakeRun(created: created, completed: completed, alertSourceType: "Disk"));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetMttrTrendAsync(null, null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Disk", result[0].IncidentCategory);
        Assert.Equal(30.0, result[0].AvgResolutionMinutes, precision: 1);
        Assert.Equal(1, result[0].Count);
    }

    [Fact]
    public async Task GetMttrTrend_TwoRunsSameCategory_AveragesCorrectly()
    {
        await using var db = CreateDb(nameof(GetMttrTrend_TwoRunsSameCategory_AveragesCorrectly));
        var day  = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        db.AgentRunRecords.Add(MakeRun(created: day, completed: day.AddMinutes(20), alertSourceType: "Network"));
        db.AgentRunRecords.Add(MakeRun(created: day, completed: day.AddMinutes(40), alertSourceType: "Network"));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetMttrTrendAsync(null, null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(30.0, result[0].AvgResolutionMinutes, precision: 1);
        Assert.Equal(2, result[0].Count);
    }

    [Fact]
    public async Task GetMttrTrend_TenantFilter_ExcludesOtherTenants()
    {
        await using var db = CreateDb(nameof(GetMttrTrend_TenantFilter_ExcludesOtherTenants));
        var day = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        db.AgentRunRecords.Add(MakeRun(tenantId: "tenant-a", created: day, completed: day.AddMinutes(10), alertSourceType: "Disk"));
        db.AgentRunRecords.Add(MakeRun(tenantId: "tenant-b", created: day, completed: day.AddMinutes(60), alertSourceType: "CPU"));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetMttrTrendAsync(null, null, "tenant-a", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Disk", result[0].IncidentCategory);
    }

    // ── GetVerifiedSavingsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetVerifiedSavings_NoRuns_ReturnsZeroReport()
    {
        await using var db = CreateDb(nameof(GetVerifiedSavings_NoRuns_ReturnsZeroReport));
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetVerifiedSavingsAsync(null, null, null, CancellationToken.None);

        Assert.Equal(0m, result.TotalEstimatedSavings);
        Assert.Equal(0,  result.QualifiedRunCount);
        Assert.Null(result.FromDate);
        Assert.Null(result.ToDate);
    }

    [Fact]
    public async Task GetVerifiedSavings_NonCompletedRuns_Excluded()
    {
        await using var db = CreateDb(nameof(GetVerifiedSavings_NonCompletedRuns_Excluded));
        db.AgentRunRecords.Add(MakeRun(status: "Failed", estimatedCost: 5m));
        db.AgentRunRecords.Add(MakeRun(status: "Proposed", estimatedCost: 3m));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetVerifiedSavingsAsync(null, null, null, CancellationToken.None);

        Assert.Equal(0m, result.TotalEstimatedSavings);
        Assert.Equal(0,  result.QualifiedRunCount);
    }

    [Fact]
    public async Task GetVerifiedSavings_CompletedRuns_SumsEstimatedCost()
    {
        await using var db = CreateDb(nameof(GetVerifiedSavings_CompletedRuns_SumsEstimatedCost));
        var day1 = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero);
        db.AgentRunRecords.Add(MakeRun(status: "Completed", created: day1, estimatedCost: 2.50m));
        db.AgentRunRecords.Add(MakeRun(status: "Completed", created: day2, estimatedCost: 7.50m));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetVerifiedSavingsAsync(null, null, null, CancellationToken.None);

        Assert.Equal(10.00m, result.TotalEstimatedSavings);
        Assert.Equal(2,      result.QualifiedRunCount);
        Assert.Equal(new DateOnly(2025, 1, 10), result.FromDate);
        Assert.Equal(new DateOnly(2025, 1, 20), result.ToDate);
    }

    // ── GetTopIncidentCategoriesAsync ──────────────────────────────────────

    [Fact]
    public async Task GetTopIncidentCategories_NoRuns_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetTopIncidentCategories_NoRuns_ReturnsEmpty));
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetTopIncidentCategoriesAsync(null, null, null, 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopIncidentCategories_MaxCount_LimitsResults()
    {
        await using var db = CreateDb(nameof(GetTopIncidentCategories_MaxCount_LimitsResults));
        var day = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        foreach (var cat in new[] { "Disk", "CPU", "Memory", "Network" })
            db.AgentRunRecords.Add(MakeRun(alertSourceType: cat, created: day));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        // 4 distinct categories; request only top 2
        var result = await sut.GetTopIncidentCategoriesAsync(null, null, null, 2, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTopIncidentCategories_OrderedByCount_Descending()
    {
        await using var db = CreateDb(nameof(GetTopIncidentCategories_OrderedByCount_Descending));
        var day = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        // CPU has 3 runs, Disk has 1 run
        db.AgentRunRecords.Add(MakeRun(alertSourceType: "Disk", created: day));
        db.AgentRunRecords.Add(MakeRun(alertSourceType: "CPU",  created: day));
        db.AgentRunRecords.Add(MakeRun(alertSourceType: "CPU",  created: day));
        db.AgentRunRecords.Add(MakeRun(alertSourceType: "CPU",  created: day));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetTopIncidentCategoriesAsync(null, null, null, 10, CancellationToken.None);

        // CPU (3) should be first, Disk (1) second
        Assert.Equal("CPU",  result[0].Category);
        Assert.Equal("Disk", result[1].Category);
        Assert.Equal(3, result[0].IncidentCount);
    }

    [Fact]
    public async Task GetTopIncidentCategories_NullAlertSourceType_MappedToUnknown()
    {
        await using var db = CreateDb(nameof(GetTopIncidentCategories_NullAlertSourceType_MappedToUnknown));
        var day = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        db.AgentRunRecords.Add(MakeRun(alertSourceType: null, created: day));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetTopIncidentCategoriesAsync(null, null, null, 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Unknown", result[0].Category);
    }

    [Fact]
    public async Task GetTopIncidentCategories_NoCompletedTimestamp_AvgResolutionIsNull()
    {
        await using var db = CreateDb(nameof(GetTopIncidentCategories_NoCompletedTimestamp_AvgResolutionIsNull));
        var day = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        // Run exists but was never completed (CompletedAtUtc is null)
        db.AgentRunRecords.Add(MakeRun(alertSourceType: "Disk", created: day, completed: null, status: "Proposed"));
        await db.SaveChangesAsync();
        var sut = new SqlOperationalDashboardQueryService(db);

        var result = await sut.GetTopIncidentCategoriesAsync(null, null, null, 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Null(result[0].AvgResolutionMinutes);
    }
}
