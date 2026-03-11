using OpsCopilot.BuildingBlocks.Contracts.Rag;

namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Post-retrieval ACL gate: returns only hits the caller is authorized to see.
/// Implementations must be deterministic, pure, and never throw.
/// The empty-after-filter case is valid and expected — callers must handle it.
/// </summary>
public interface IRunbookAclFilter
{
    IReadOnlyList<RunbookSearchHit> Filter(
        IReadOnlyList<RunbookSearchHit> hits,
        RunbookCallerContext caller);
}
