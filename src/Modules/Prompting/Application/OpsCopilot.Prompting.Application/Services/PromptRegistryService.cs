using OpsCopilot.Prompting.Application.Abstractions;
using OpsCopilot.Prompting.Domain.Entities;
using OpsCopilot.Prompting.Domain.Repositories;

namespace OpsCopilot.Prompting.Application.Services;

/// <summary>
/// Resolves prompt templates via the repository.
/// Returns <c>null</c> when the table has no active row for the requested key —
/// callers must apply their own hardcoded default in that case.
/// </summary>
internal sealed class PromptRegistryService(IPromptTemplateRepository repository) : IPromptRegistry
{
    public Task<PromptTemplate?> ResolveAsync(string promptKey, CancellationToken ct = default)
        => repository.FindActiveAsync(promptKey, ct);
}
