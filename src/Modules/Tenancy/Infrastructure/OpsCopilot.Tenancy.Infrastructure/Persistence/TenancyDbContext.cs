using Microsoft.EntityFrameworkCore;
using OpsCopilot.Tenancy.Domain.Entities;

namespace OpsCopilot.Tenancy.Infrastructure.Persistence;

public sealed class TenancyDbContext : DbContext
{
    public TenancyDbContext(DbContextOptions<TenancyDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantConfigEntry> TenantConfigEntries => Set<TenantConfigEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenancy");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.TenantId);
            e.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<TenantConfigEntry>(e =>
        {
            e.HasKey(x => x.TenantConfigEntryId);
            e.Property(x => x.Key).HasMaxLength(128).IsRequired();
            e.Property(x => x.Value).HasMaxLength(1024).IsRequired();
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });
    }
}
