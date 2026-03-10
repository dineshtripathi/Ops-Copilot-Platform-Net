using Microsoft.EntityFrameworkCore;
using OpsCopilot.Packs.Domain.Entities;

namespace OpsCopilot.Packs.Infrastructure.Persistence;

public sealed class PacksDbContext : DbContext
{
    public PacksDbContext(DbContextOptions<PacksDbContext> options) : base(options) { }

    public DbSet<ProposalDeadLetterEntry> ProposalDeadLetterEntries => Set<ProposalDeadLetterEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("packs");

        modelBuilder.Entity<ProposalDeadLetterEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.AttemptId).IsUnique();
            entity.HasIndex(e => e.TenantId);

            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PackName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ActionId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ActionType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ParametersJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReplayError).HasColumnType("nvarchar(max)");
            entity.Property(e => e.DeadLetteredAt).HasColumnType("datetimeoffset");
            entity.Property(e => e.LastReplayedAt).HasColumnType("datetimeoffset");
        });
    }
}
