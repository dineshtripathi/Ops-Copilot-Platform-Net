using OpsCopilot.Governance.Application.Models;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Resolves the session time-to-live for a given tenant.
/// The orchestrator uses this to create or extend sessions.
/// </summary>
public interface ISessionPolicy
{
    /// <summary>
    /// Returns the configured session TTL for the given tenant.
    /// Tenant overrides take precedence over defaults.
    /// </summary>
    TimeSpan GetSessionTtl(string tenantId);
}
