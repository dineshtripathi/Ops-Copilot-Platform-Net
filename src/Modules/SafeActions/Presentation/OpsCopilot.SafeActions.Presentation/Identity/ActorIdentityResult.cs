namespace OpsCopilot.SafeActions.Presentation.Identity;

/// <summary>
/// Represents the resolved actor identity for an HTTP request.
/// </summary>
/// <param name="ActorId">The resolved actor identifier.</param>
/// <param name="Source">How the identity was resolved: "claim", "header", or "anonymous".</param>
/// <param name="IsAuthenticated">Whether the identity came from an authenticated principal.</param>
public sealed record ActorIdentityResult(
    string ActorId,
    string Source,
    bool IsAuthenticated);
