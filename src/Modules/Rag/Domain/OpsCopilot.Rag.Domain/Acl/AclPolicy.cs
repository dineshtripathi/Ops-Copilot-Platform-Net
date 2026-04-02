namespace OpsCopilot.Rag.Domain.Acl;

/// <summary>
/// Represents an access control policy scoped to a tenant (§6.17).
/// The ACL service holds all policies as the single source of truth.
/// Enforcement is delegated to the Vector Store Access Layer (§6.8)
/// which consumes the <see cref="AclFilter"/> produced from this policy.
/// </summary>
public sealed class AclPolicy
{
    /// <summary>Stable, unique identifier for this policy.</summary>
    public required string PolicyId { get; init; }

    /// <summary>Tenant that owns this policy.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Environments (e.g. "production", "staging") in which this policy is active.
    /// An empty list means the policy applies to all environments.
    /// </summary>
    public IReadOnlyList<string> AllowedEnvironments { get; init; } = [];

    /// <summary>
    /// Claim values that must be present in the caller's identity for this policy to grant access.
    /// An empty list means access is granted without additional claim requirements.
    /// </summary>
    public IReadOnlyList<string> RequiredClaims { get; init; } = [];
}
