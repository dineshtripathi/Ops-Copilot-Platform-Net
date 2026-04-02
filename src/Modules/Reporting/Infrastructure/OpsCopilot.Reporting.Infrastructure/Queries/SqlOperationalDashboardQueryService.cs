using Microsoft.EntityFrameworkCore;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

namespace OpsCopilot.Reporting.Infrastructure.Queries;

/// <summary>
/// Slice 193 — SQL-backed implementation of IOperationalDashboardQueryService.
/// Queries agentRuns.AgentRuns for MTTR trends, verified savings, and incident categories.
/// All queries are read-only (NoTracking set on context); no mutations.
/// </summary>
internal sealed class SqlOperationalDashboardQueryService(
    ReportingReadDbContext db) : IOperationalDashboardQueryService
{
    // ── MTTR Trend ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MttrTrendPoint>> GetMttrTrendAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyRunFilters(
            db.AgentRunRecords.Where(r => r.CompletedAtUtc.HasValue),
            fromUtc, toUtc, tenantId);

        // Fetch raw rows; group + aggregate in memory to avoid EF translation issues with TimeSpan
        var raw = await query
            .Select(r => new
            {
                r.CreatedAtUtc,
                r.CompletedAtUtc,
                r.AlertSourceType,
            })
            .ToListAsync(ct);

        return raw
            .GroupBy(r => (
                BucketDate: DateOnly.FromDateTime(r.CreatedAtUtc.UtcDateTime),
                Category:   r.AlertSourceType))
            .Select(g => new MttrTrendPoint(
                g.Key.BucketDate,
                g.Key.Category,
                g.Average(r => (r.CompletedAtUtc!.Value - r.CreatedAtUtc).TotalMinutes),
                g.Count()))
            .OrderBy(p => p.BucketDate)
            .ToList();
    }

    // ── Verified Savings ───────────────────────────────────────────────────

    public async Task<VerifiedSavingsReport> GetVerifiedSavingsAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyRunFilters(
            db.AgentRunRecords.Where(r => r.Status == "Completed"),
            fromUtc, toUtc, tenantId);

        var raw = await query
            .Select(r => new { r.CreatedAtUtc, r.EstimatedCost })
            .ToListAsync(ct);

        if (raw.Count == 0)
            return new VerifiedSavingsReport(0m, 0, null, null);

        var minDate = DateOnly.FromDateTime(raw.Min(r => r.CreatedAtUtc.UtcDateTime));
        var maxDate = DateOnly.FromDateTime(raw.Max(r => r.CreatedAtUtc.UtcDateTime));
        var totalSavings = raw.Sum(r => r.EstimatedCost ?? 0m);

        return new VerifiedSavingsReport(totalSavings, raw.Count, minDate, maxDate);
    }

    // ── Top Incident Categories ────────────────────────────────────────────

    public async Task<IReadOnlyList<IncidentCategoryRow>> GetTopIncidentCategoriesAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, int maxCount, CancellationToken ct)
    {
        var query = ApplyRunFilters(db.AgentRunRecords, fromUtc, toUtc, tenantId);

        var raw = await query
            .Select(r => new { r.AlertSourceType, r.CompletedAtUtc, r.CreatedAtUtc })
            .ToListAsync(ct);

        return raw
            .GroupBy(r => r.AlertSourceType ?? "Unknown")
            .Select(g =>
            {
                var resolutions = g
                    .Where(r => r.CompletedAtUtc.HasValue)
                    .Select(r => (r.CompletedAtUtc!.Value - r.CreatedAtUtc).TotalMinutes)
                    .ToList();

                double? avgResolution = resolutions.Count > 0
                    ? resolutions.Average()
                    : null;

                return new IncidentCategoryRow(g.Key, g.Count(), avgResolution);
            })
            .OrderByDescending(r => r.IncidentCount)
            .Take(maxCount)
            .ToList();
    }

    // ── Shared filter helper ──────────────────────────────────────────────

    private static IQueryable<AgentRunReadModel> ApplyRunFilters(
        IQueryable<AgentRunReadModel> query,
        DateTime? fromUtc, DateTime? toUtc, string? tenantId)
    {
        if (fromUtc.HasValue)
            query = query.Where(r => r.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(r => r.CreatedAtUtc <= toUtc.Value);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(r => r.TenantId == tenantId);
        return query;
    }
}
