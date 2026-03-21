using Microsoft.EntityFrameworkCore;
using OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

namespace OpsCopilot.Reporting.Infrastructure.Persistence;

/// <summary>
/// Read-only DbContext that maps to the existing safeActions schema.
/// No migrations are generated — this is purely a query-side projection.
/// </summary>
internal sealed class ReportingReadDbContext : DbContext
{
    public ReportingReadDbContext(DbContextOptions<ReportingReadDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<ActionRecordReadModel> ActionRecords => Set<ActionRecordReadModel>();
    public DbSet<AgentRunReadModel> AgentRunRecords => Set<AgentRunReadModel>();
    public DbSet<ToolCallReadModel> ToolCallRecords => Set<ToolCallReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("safeActions");

        modelBuilder.Entity<ActionRecordReadModel>(e =>
        {
            e.ToTable("ActionRecords");
            e.HasKey(a => a.ActionRecordId);
            e.Property(a => a.TenantId).HasMaxLength(128);
            e.Property(a => a.ActionType).HasMaxLength(128);
            e.Property(a => a.Status).HasMaxLength(32);
            e.Property(a => a.RollbackStatus).HasMaxLength(32);
        });

        modelBuilder.Entity<AgentRunReadModel>(e =>
        {
            e.ToTable("AgentRuns", "agentRuns");
            e.HasKey(a => a.RunId);
            e.Property(a => a.TenantId).HasMaxLength(128);
            e.Property(a => a.Status).HasMaxLength(32);
            e.Property(a => a.CitationsJson).HasColumnType("nvarchar(max)");
            e.Property(a => a.SummaryJson).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<ToolCallReadModel>(e =>
        {
            e.ToTable("ToolCalls", "agentRuns");
            e.HasKey(t => t.ToolCallId);
            e.Property(t => t.ToolName).HasMaxLength(128);
            e.Property(t => t.Status).HasMaxLength(32);
        });
    }
}
