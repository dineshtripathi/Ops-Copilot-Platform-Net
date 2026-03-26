using OpsCopilot.Prompting.Domain.Entities;

namespace OpsCopilot.Prompting.Application.Abstractions;

/// <summary>
/// Resolves the active <see cref="PromptTemplate"/> for a given prompt key.
/// </summary>
public interface IPromptRegistry
{
    /// <summary>
    /// Returns the active template for <paramref name="promptKey"/>,
    /// or <c>null</c> when no template has been seeded yet.
    /// Callers should fall back to a hardcoded default when <c>null</c> is returned.
    /// </summary>
    Task<PromptTemplate?> ResolveAsync(string promptKey, CancellationToken ct = default);
}
