using Microsoft.EntityFrameworkCore;

namespace OpsCopilot.Evaluation.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Evaluation module. Slice 195.
/// Schema: <c>eval</c>.
/// </summary>
internal sealed class EvaluationDbContext(DbContextOptions<EvaluationDbContext> options)
    : DbContext(options)
{
    internal DbSet<OnlineEvalRow> OnlineEvalEntries => Set<OnlineEvalRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("eval");

        modelBuilder.Entity<OnlineEvalRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ModelVersion).HasMaxLength(128).IsRequired();
            e.Property(x => x.PromptVersionId).HasMaxLength(128).IsRequired();
            e.ToTable("OnlineEvalEntries");
        });
    }
}
