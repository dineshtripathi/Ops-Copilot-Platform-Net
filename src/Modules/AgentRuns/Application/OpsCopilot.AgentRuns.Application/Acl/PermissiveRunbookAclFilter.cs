using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.BuildingBlocks.Contracts.Rag;

namespace OpsCopilot.AgentRuns.Application.Acl;

/// <summary>
/// Pass-through ACL filter: authorizes all hits unconditionally.
/// Correct for this slice — MCP wire format does not yet carry Tier/AllowedGroups/AllowedRoles.
/// A future slice will replace this with a real evaluator once those fields are populated.
/// </summary>
internal sealed class PermissiveRunbookAclFilter : IRunbookAclFilter
{
    public IReadOnlyList<RunbookSearchHit> Filter(
        IReadOnlyList<RunbookSearchHit> hits,
        RunbookCallerContext caller) => hits;
}
