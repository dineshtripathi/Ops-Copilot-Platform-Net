namespace OpsCopilot.Rag.Domain.Acl;

/// <summary>
/// The filter expression resolved by the ACL service for a given tenant and user context.
/// The filter expression is applied by the Vector Store Access Layer (§6.8) at query time.
/// An empty <see cref="FilterExpression"/> represents a passthrough / allow-all policy.
/// </summary>
/// <param name="FilterExpression">
/// OData or vendor-specific filter string appended to vector search queries.
/// Empty string means no ACL restriction (development / trusted internal callers).
/// </param>
/// <param name="AclPolicyId">
/// Stable identifier of the <see cref="AclPolicy"/> that produced this filter.
/// Empty when no explicit policy was matched.
/// </param>
public sealed record AclFilter(string FilterExpression, string AclPolicyId);
