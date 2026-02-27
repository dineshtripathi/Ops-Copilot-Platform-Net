using Microsoft.EntityFrameworkCore;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Infrastructure.Persistence;

namespace OpsCopilot.Tenancy.Infrastructure.Repositories;

public sealed class SqlTenantRegistry : ITenantRegistry
{
    private readonly TenancyDbContext _db;

    public SqlTenantRegistry(TenancyDbContext db) => _db = db;

    public async Task<Tenant> CreateAsync(string displayName, string? updatedBy, CancellationToken ct = default)
    {
        var tenant = Tenant.Create(displayName, updatedBy);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().OrderBy(t => t.DisplayName).ToListAsync(ct);

    public async Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
}
