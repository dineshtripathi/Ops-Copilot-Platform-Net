using Microsoft.EntityFrameworkCore;
using OpsCopilot.Prompting.Domain.Entities;

namespace OpsCopilot.Prompting.Infrastructure.Persistence;

public sealed class PromptingDbContext : DbContext
{
    public PromptingDbContext(DbContextOptions<PromptingDbContext> options) : base(options) { }

    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prompting");

        modelBuilder.Entity<PromptTemplate>(e =>
        {
            e.HasKey(x => x.PromptTemplateId);
            e.Property(x => x.PromptKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.Content).HasMaxLength(8000).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            // Partial index: only one active row per key is enforced at the application layer.
            e.HasIndex(x => new { x.PromptKey, x.IsActive });
        });
    }
}
