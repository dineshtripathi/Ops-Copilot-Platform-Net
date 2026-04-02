using Microsoft.EntityFrameworkCore;
using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Application.Models;
using OpsCopilot.Prompting.Infrastructure.Persistence;

namespace OpsCopilot.Prompting.Infrastructure.Repositories;

/// <summary>
/// SQL Server-backed implementation of <see cref="ICanaryStore"/>.
/// Registered as Singleton; creates a short-lived <see cref="PromptingDbContext"/>
/// per call using pre-built options, keeping thread safety without a factory.
/// </summary>
internal sealed class SqlCanaryStore(DbContextOptions<PromptingDbContext> options) : ICanaryStore
{
    public CanaryState? GetCanary(string promptKey)
    {
        using var db = new PromptingDbContext(options);
        var row = db.CanaryExperiments.Find(promptKey);
        return row is null ? null
            : new CanaryState(row.PromptKey, row.CandidateVersion, row.CandidateContent,
                              row.TrafficPercent, row.StartedAt);
    }

    public void SetCanary(string promptKey, CanaryState state)
    {
        using var db = new PromptingDbContext(options);
        var existing = db.CanaryExperiments.Find(promptKey);
        if (existing is null)
        {
            db.CanaryExperiments.Add(new CanaryExperimentRow
            {
                PromptKey        = promptKey,
                CandidateVersion = state.CandidateVersion,
                CandidateContent = state.CandidateContent,
                TrafficPercent   = state.TrafficPercent,
                StartedAt        = state.StartedAt
            });
        }
        else
        {
            existing.CandidateVersion = state.CandidateVersion;
            existing.CandidateContent = state.CandidateContent;
            existing.TrafficPercent   = state.TrafficPercent;
            existing.StartedAt        = state.StartedAt;
        }
        db.SaveChanges();
    }

    public void RemoveCanary(string promptKey)
    {
        using var db = new PromptingDbContext(options);
        var row = db.CanaryExperiments.Find(promptKey);
        if (row is not null)
        {
            db.CanaryExperiments.Remove(row);
            db.SaveChanges();
        }
    }
}
