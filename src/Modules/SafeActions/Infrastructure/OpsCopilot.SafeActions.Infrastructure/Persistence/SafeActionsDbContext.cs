using Microsoft.EntityFrameworkCore;
using OpsCopilot.SafeActions.Domain.Entities;

namespace OpsCopilot.SafeActions.Infrastructure.Persistence;

public sealed class SafeActionsDbContext : DbContext
{
    public SafeActionsDbContext(DbContextOptions<SafeActionsDbContext> options)
        : base(options) { }

    public DbSet<ActionRecord>   ActionRecords   => Set<ActionRecord>();
    public DbSet<ApprovalRecord> ApprovalRecords => Set<ApprovalRecord>();
    public DbSet<ExecutionLog>   ExecutionLogs   => Set<ExecutionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("safeActions");

        // ── ActionRecord ────────────────────────────────────────────
        modelBuilder.Entity<ActionRecord>(e =>
        {
            e.HasKey(x => x.ActionRecordId);

            e.Property(x => x.TenantId).HasMaxLength(128);
            e.Property(x => x.ActionType).HasMaxLength(128);
            e.Property(x => x.ProposedPayloadJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ExecutionPayloadJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.OutcomeJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.RollbackPayloadJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.RollbackOutcomeJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ManualRollbackGuidance).HasColumnType("nvarchar(max)");

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            e.Property(x => x.RollbackStatus)
                .HasConversion<string>()
                .HasMaxLength(32);

            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.TenantId, x.Status });
        });

        // ── ApprovalRecord ──────────────────────────────────────────
        modelBuilder.Entity<ApprovalRecord>(e =>
        {
            e.HasKey(x => x.ApprovalId);

            e.Property(x => x.ApproverIdentity).HasMaxLength(256);
            e.Property(x => x.Decision)
                .HasConversion<string>()
                .HasMaxLength(32);
            e.Property(x => x.Reason).HasColumnType("nvarchar(max)");
            e.Property(x => x.Target).HasMaxLength(32);

            e.HasIndex(x => x.ActionRecordId);
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // ── ExecutionLog ────────────────────────────────────────────
        modelBuilder.Entity<ExecutionLog>(e =>
        {
            e.HasKey(x => x.ExecutionLogId);

            e.Property(x => x.ExecutionType).HasMaxLength(32);
            e.Property(x => x.RequestPayloadJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResponsePayloadJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasMaxLength(32);

            e.HasIndex(x => x.ActionRecordId);
            e.HasIndex(x => x.ExecutedAtUtc);
        });
    }
}
