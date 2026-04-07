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
    public DbSet<AgentRunPolicyEvent> PolicyEvents => Set<AgentRunPolicyEvent>();
    public DbSet<AgentRunFeedback> Feedbacks => Set<AgentRunFeedback>();
    public DbSet<SessionEntry> Sessions => Set<SessionEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("agentRuns");

        modelBuilder.Entity<AgentRun>(e =>
        {
            e.HasKey(x => x.RunId);
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.AlertFingerprint).HasMaxLength(64);
            e.Property(x => x.AlertProvider).HasMaxLength(64);
            e.Property(x => x.AlertSourceType).HasMaxLength(64);
            e.Property(x => x.AzureSubscriptionId).HasMaxLength(64);
            e.Property(x => x.AzureResourceGroup).HasMaxLength(128);
            e.Property(x => x.AzureResourceId).HasMaxLength(512);
            e.Property(x => x.AzureApplication).HasMaxLength(128);
            e.Property(x => x.AzureWorkspaceId).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SummaryJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.CitationsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.AlertFingerprint);
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.TenantId, x.IsExceptionSignal, x.CreatedAtUtc });
            e.HasIndex(x => new { x.TenantId, x.AzureResourceGroup, x.CreatedAtUtc });
            e.Property(x => x.SessionId);
            e.HasIndex(x => x.SessionId);
            e.Property(x => x.ModelId).HasMaxLength(128);
            e.Property(x => x.PromptVersionId).HasMaxLength(64);
            e.Property(x => x.EstimatedCost).HasColumnType("decimal(18,6)");
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

        modelBuilder.Entity<AgentRunPolicyEvent>(e =>
        {
            e.HasKey(x => x.PolicyEventId);
            e.Property(x => x.PolicyName).HasMaxLength(128).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.Message).HasColumnType("nvarchar(max)").IsRequired();
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.OccurredAtUtc);
        });

        // Sessions — persisted TTL sessions for SQL-backed ISessionStore
        modelBuilder.Entity<SessionEntry>(e =>
        {
            e.ToTable("Sessions", "agentRuns");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.ExpiresAtUtc).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ExpiresAtUtc });
        });

        // Slice 123 — operator feedback (INSERT-only, never updated)
        modelBuilder.Entity<AgentRunFeedback>(e =>
        {
            e.ToTable("RunFeedback", "agentRuns");
            e.HasKey(x => x.FeedbackId);
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Rating).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasIndex(x => x.RunId).IsUnique();  // one feedback per run
            e.HasIndex(x => new { x.TenantId, x.SubmittedAtUtc });
        });
    }
}
