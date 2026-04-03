using Microsoft.EntityFrameworkCore;
using OpsCopilot.Prompting.Domain.Entities;
using OpsCopilot.Prompting.Domain.Repositories;
using OpsCopilot.Prompting.Infrastructure.Persistence;

namespace OpsCopilot.Prompting.Infrastructure.Repositories;

internal sealed class SqlPromptTemplateRepository(PromptingDbContext db) : IPromptTemplateRepository
{
    public Task<PromptTemplate?> FindActiveAsync(string promptKey, CancellationToken ct = default)
        => db.PromptTemplates
             .Where(p => p.PromptKey == promptKey && p.IsActive)
             .OrderByDescending(p => p.Version)
             .FirstOrDefaultAsync(ct);
}
