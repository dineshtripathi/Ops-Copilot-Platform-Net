using OpsCopilot.Rag.Domain;

namespace OpsCopilot.Rag.Application.Memory;

/// <summary>
/// Indexes completed incident triage results into the vector store.
/// </summary>
public interface IIncidentMemoryIndexer
{
    /// <summary>
    /// Embeds and upserts a single <see cref="IncidentMemoryDocument"/> into the store.
    /// The caller is responsible for populating all non-embedding fields before calling.
    /// </summary>
    Task IndexAsync(
        IncidentMemoryDocument document,
        CancellationToken      cancellationToken = default);
}
