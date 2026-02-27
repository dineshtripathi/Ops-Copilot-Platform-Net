using Microsoft.EntityFrameworkCore;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Infrastructure.Persistence;

namespace OpsCopilot.Tenancy.Infrastructure.Repositories;

public sealed class SqlTenantConfigStore : ITenantConfigStore
{
    private readonly TenancyDbContext _db;

    public SqlTenantConfigStore(TenancyDbContext db) => _db = db;

    public async Task UpsertAsync(Guid tenantId, string key, string value, string? updatedBy, CancellationToken ct = default)
    {
        var existing = await _db.TenantConfigEntries
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Key == key, ct);

        if (existing is not null)
        {
            existing.Update(value, updatedBy);
        }
        else
        {
            _db.TenantConfigEntries.Add(TenantConfigEntry.Create(tenantId, key, value, updatedBy));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TenantConfigEntry>> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.TenantConfigEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Key)
            .ToListAsync(ct);
}
