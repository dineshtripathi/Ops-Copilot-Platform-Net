using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace OpsCopilot.SafeActions.Presentation.Identity;

/// <summary>
/// Claims-first actor identity resolver.
/// <para>
///   Precedence chain:
///   1. ClaimTypes.NameIdentifier  ("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" — maps to "oid" / "sub")
///   2. "oid" claim               (Azure AD object ID)
///   3. "sub" claim               (standard OIDC subject)
///   4. ClaimTypes.Name / "preferred_username"
///   5. <c>x-actor-id</c> header  (only when <c>SafeActions:AllowActorHeaderFallback</c> is <c>true</c>)
///   6. <c>"unknown"</c>          (only when <c>SafeActions:AllowAnonymousActorFallback</c> is <c>true</c>)
///   7. <c>null</c>               (caller should return 401)
/// </para>
/// </summary>
public sealed class ClaimsActorIdentityResolver : IActorIdentityResolver
{
    private readonly bool _allowHeaderFallback;
    private readonly bool _allowAnonymousFallback;

    public ClaimsActorIdentityResolver(IConfiguration configuration)
    {
        _allowHeaderFallback   = configuration.GetValue<bool>("SafeActions:AllowActorHeaderFallback");
        _allowAnonymousFallback = configuration.GetValue<bool>("SafeActions:AllowAnonymousActorFallback");
    }

    /// <inheritdoc />
    public ActorIdentityResult? Resolve(HttpContext context)
    {
        var principal = context.User;

        // ── Claims-based resolution (authenticated) ──────────────
        if (principal?.Identity?.IsAuthenticated == true)
        {
            var id = GetClaimValue(principal, ClaimTypes.NameIdentifier)
                  ?? GetClaimValue(principal, "oid")
                  ?? GetClaimValue(principal, "sub")
                  ?? GetClaimValue(principal, ClaimTypes.Name)
                  ?? GetClaimValue(principal, "preferred_username");

            if (!string.IsNullOrWhiteSpace(id))
                return new ActorIdentityResult(id, "claim", IsAuthenticated: true);
        }

        // ── Header fallback (dev / test convenience) ─────────────
        if (_allowHeaderFallback)
        {
            var header = context.Request.Headers["x-actor-id"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
                return new ActorIdentityResult(header, "header", IsAuthenticated: false);
        }

        // ── Anonymous fallback ───────────────────────────────────
        if (_allowAnonymousFallback)
            return new ActorIdentityResult("unknown", "anonymous", IsAuthenticated: false);

        // ── No identity available ────────────────────────────────
        return null;
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
