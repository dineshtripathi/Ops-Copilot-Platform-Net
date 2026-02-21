using OpsCopilot.Governance.Application.Models;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Maps a runtime exception to a deterministic degraded-mode outcome.
/// Used AFTER an MCP call fails — classifies the error for audit and response.
/// </summary>
public interface IDegradedModePolicy
{
    DegradedDecision MapFailure(Exception ex);
}
