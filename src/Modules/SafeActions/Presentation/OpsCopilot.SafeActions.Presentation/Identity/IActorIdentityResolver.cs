using Microsoft.AspNetCore.Http;

namespace OpsCopilot.SafeActions.Presentation.Identity;

/// <summary>
/// Resolves the actor identity from the current HTTP request.
/// Returns <c>null</c> when no identity can be determined and
/// all fallback paths are disabled.
/// </summary>
public interface IActorIdentityResolver
{
    /// <summary>
    /// Attempt to resolve the actor identity from the given HTTP context.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>
    /// An <see cref="ActorIdentityResult"/> when identity is resolved;
    /// <c>null</c> when no identity is available and fallbacks are disabled.
    /// </returns>
    ActorIdentityResult? Resolve(HttpContext context);
}
