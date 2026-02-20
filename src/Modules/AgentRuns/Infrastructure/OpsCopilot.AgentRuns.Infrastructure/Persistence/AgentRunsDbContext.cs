using Microsoft.EntityFrameworkCore;
using OpsCopilot.AgentRuns.Domain.Entities;

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the AgentRuns module.
/// Schema: [agentRuns] — keeps module tables isolated from other schemas.
///
/// Table design enforces the append-only contract:
///   - AgentRuns: only Status/CompletedAtUtc/SummaryJson/CitationsJson are ever UPDATEd.
///   - ToolCalls: INSERT-only; no UPDATE path exists in SqlAgentRunRepository.
/// </summary>
public sealed class AgentRunsDbContext : DbContext
{
    public AgentRunsDbContext(DbContextOptions<AgentRunsDbContext> options) : base(options) { }

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("agentRuns");

        modelBuilder.Entity<AgentRun>(e =>
        {
            e.HasKey(x => x.RunId);
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.AlertFingerprint).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SummaryJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.CitationsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.AlertFingerprint);
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<ToolCall>(e =>
        {
            e.HasKey(x => x.ToolCallId);
            e.Property(x => x.ToolName).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.CitationsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.ExecutedAtUtc);
        });
    }
}
