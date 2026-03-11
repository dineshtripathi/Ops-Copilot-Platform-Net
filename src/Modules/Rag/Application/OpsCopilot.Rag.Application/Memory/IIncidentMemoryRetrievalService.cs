namespace OpsCopilot.Rag.Application.Memory;

/// <summary>
/// Searches the incident vector store for similar prior incidents.
/// Implementations must enforce tenant isolation on every search.
/// </summary>
public interface IIncidentMemoryRetrievalService
{
    /// <summary>
    /// Returns up to <see cref="IncidentMemoryQuery.MaxResults"/> hits for the given tenant,
    /// all with score ≥ <see cref="IncidentMemoryQuery.MinScore"/>.
    /// Never throws — returns empty on failure.
    /// </summary>
    Task<IReadOnlyList<IncidentMemoryHit>> SearchAsync(
        IncidentMemoryQuery query,
        CancellationToken   cancellationToken = default);
}
