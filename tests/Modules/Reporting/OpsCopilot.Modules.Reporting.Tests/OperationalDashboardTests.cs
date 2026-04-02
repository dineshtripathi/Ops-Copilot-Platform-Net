using Xunit;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure;

namespace OpsCopilot.Modules.Reporting.Tests;

public sealed class OperationalDashboardTests
{
    private readonly NullOperationalDashboardQueryService _sut = new();

    // ── MTTR ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMttrTrend_NullImpl_ReturnsEmptyList()
    {
        var result = await _sut.GetMttrTrendAsync(null, null, null, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMttrTrend_NullImpl_WithDateRange_ReturnsEmptyList()
    {
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetMttrTrendAsync(from, to, "tenant-a", CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Verified Savings ───────────────────────────────────────────────────

    [Fact]
    public async Task GetVerifiedSavings_NullImpl_ReturnsZeroReport()
    {
        var result = await _sut.GetVerifiedSavingsAsync(null, null, null, CancellationToken.None);

        Assert.Equal(0m, result.TotalEstimatedSavings);
        Assert.Equal(0,  result.QualifiedRunCount);
        Assert.Null(result.FromDate);
        Assert.Null(result.ToDate);
    }

    // ── Incident Categories ────────────────────────────────────────────────

    [Fact]
    public async Task GetTopIncidentCategories_NullImpl_ReturnsEmptyList()
    {
        var result = await _sut.GetTopIncidentCategoriesAsync(null, null, null, 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopIncidentCategories_NullImpl_MaxCountIgnored_StillReturnsEmpty()
    {
        var result = await _sut.GetTopIncidentCategoriesAsync(null, null, null, 1, CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Domain model contracts ─────────────────────────────────────────────

    [Fact]
    public void MttrTrendPoint_Construction_HoldsValues()
    {
        var point = new MttrTrendPoint(new DateOnly(2025, 1, 15), "Disk", 12.5, 4);

        Assert.Equal(new DateOnly(2025, 1, 15), point.BucketDate);
        Assert.Equal("Disk", point.IncidentCategory);
        Assert.Equal(12.5,   point.AvgResolutionMinutes);
        Assert.Equal(4,      point.Count);
    }

    [Fact]
    public void VerifiedSavingsReport_Construction_HoldsValues()
    {
        var report = new VerifiedSavingsReport(250.00m, 5, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));

        Assert.Equal(250.00m,               report.TotalEstimatedSavings);
        Assert.Equal(5,                     report.QualifiedRunCount);
        Assert.Equal(new DateOnly(2025, 1, 1),  report.FromDate);
        Assert.Equal(new DateOnly(2025, 3, 31), report.ToDate);
    }

    [Fact]
    public void IncidentCategoryRow_Construction_HoldsValues()
    {
        var row = new IncidentCategoryRow("CPU", 12, 8.3);

        Assert.Equal("CPU", row.Category);
        Assert.Equal(12,    row.IncidentCount);
        Assert.Equal(8.3,   row.AvgResolutionMinutes);
    }

    [Fact]
    public void IncidentCategoryRow_NullableResolution_IsAccepted()
    {
        var row = new IncidentCategoryRow("Network", 3, null);

        Assert.Equal("Network", row.Category);
        Assert.Null(row.AvgResolutionMinutes);
    }
}
