using OpsCopilot.Prompting.Domain.Entities;

namespace OpsCopilot.Prompting.Domain.Repositories;

/// <summary>
/// Reads prompt templates from persistent storage.
/// Write operations are handled by the administration API (future slice).
/// </summary>
public interface IPromptTemplateRepository
{
    /// <summary>
    /// Returns the single active template for <paramref name="promptKey"/>,
    /// or <c>null</c> if none exists.
    /// </summary>
    Task<PromptTemplate?> FindActiveAsync(string promptKey, CancellationToken ct = default);
}
