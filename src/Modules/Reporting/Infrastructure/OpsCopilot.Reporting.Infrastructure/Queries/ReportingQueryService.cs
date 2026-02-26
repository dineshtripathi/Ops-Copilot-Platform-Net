using Microsoft.EntityFrameworkCore;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

namespace OpsCopilot.Reporting.Infrastructure.Queries;

internal sealed class ReportingQueryService : IReportingQueryService
{
    private readonly ReportingReadDbContext _db;

    public ReportingQueryService(ReportingReadDbContext db) => _db = db;

    public async Task<SafeActionsSummaryReport> GetSummaryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyFilters(_db.ActionRecords, fromUtc, toUtc, tenantId);

        var statusCounts = await query
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(string status) =>
            statusCounts.FirstOrDefault(s => s.Status == status)?.Count ?? 0;

        return new SafeActionsSummaryReport(
            TotalActions: statusCounts.Sum(s => s.Count),
            Proposed: Count("Proposed"),
            Approved: Count("Approved"),
            Rejected: Count("Rejected"),
            Executing: Count("Executing"),
            Completed: Count("Completed"),
            Failed: Count("Failed"),
            FromUtc: fromUtc,
            ToUtc: toUtc);
    }

    public async Task<IReadOnlyList<ActionTypeBreakdownRow>> GetByActionTypeAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct)
    {
        var query = ApplyFilters(_db.ActionRecords, fromUtc, toUtc, tenantId);

        return await query
            .GroupBy(a => a.ActionType)
            .Select(g => new ActionTypeBreakdownRow(
                g.Key,
                g.Count(),
                g.Count(a => a.Status == "Completed"),
                g.Count(a => a.Status == "Failed")))
            .OrderByDescending(r => r.Count)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TenantBreakdownRow>> GetByTenantAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var query = ApplyFilters(_db.ActionRecords, fromUtc, toUtc, tenantId: null);

        return await query
            .GroupBy(a => a.TenantId)
            .Select(g => new TenantBreakdownRow(
                g.Key,
                g.Count(),
                g.Count(a => a.Status == "Completed"),
                g.Count(a => a.Status == "Failed")))
            .OrderByDescending(r => r.TotalActions)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecentActionRow>> GetRecentAsync(
        int limit, string? tenantId, CancellationToken ct)
    {
        var query = _db.ActionRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(limit)
            .Select(a => new RecentActionRow(
                a.ActionRecordId,
                a.TenantId,
                a.ActionType,
                a.Status,
                a.RollbackStatus,
                a.CreatedAtUtc,
                a.CompletedAtUtc))
            .ToListAsync(ct);
    }

    // ── Shared filter helper ────────────────────────────────────────
    private static IQueryable<ActionRecordReadModel> ApplyFilters(
        IQueryable<ActionRecordReadModel> query,
        DateTime? fromUtc, DateTime? toUtc, string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        if (fromUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(a => a.CreatedAtUtc <= toUtc.Value);

        return query;
    }
}
