using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
using OpsCopilot.SafeActions.Presentation.Identity;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="ClaimsActorIdentityResolver"/>.
/// Validates the 7-step precedence chain, fallback behaviour, and 401-trigger.
/// </summary>
public class ClaimsActorIdentityResolverTests
{
    // ── Helpers ─────────────────────────────────────────────────────

    private static ClaimsActorIdentityResolver CreateResolver(
        bool allowHeaderFallback = false,
        bool allowAnonymousFallback = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SafeActions:AllowActorHeaderFallback"] = allowHeaderFallback.ToString(),
                ["SafeActions:AllowAnonymousActorFallback"] = allowAnonymousFallback.ToString()
            })
            .Build();

        return new ClaimsActorIdentityResolver(config);
    }

    private static HttpContext CreateHttpContext(
        ClaimsPrincipal? principal = null,
        string? actorHeader = null)
    {
        var context = new DefaultHttpContext();

        if (principal is not null)
            context.User = principal;

        if (actorHeader is not null)
            context.Request.Headers["x-actor-id"] = actorHeader;

        return context;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    // ── Claims precedence ───────────────────────────────────────────

    [Fact]
    public void Resolve_ReturnsNameIdentifier_WhenPresent()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "user-nameid"),
            new Claim("oid", "user-oid"),
            new Claim("sub", "user-sub"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("user-nameid", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_ReturnsOid_WhenNoNameIdentifier()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim("oid", "user-oid"),
            new Claim("sub", "user-sub"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("user-oid", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_ReturnsSub_WhenNoNameIdentifierOrOid()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(new Claim("sub", "user-sub"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("user-sub", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_ReturnsName_WhenOnlyNameClaim()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.Name, "user-display"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("user-display", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_ReturnsPreferredUsername_WhenOnlyPreferredUsername()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim("preferred_username", "user@contoso.com"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("user@contoso.com", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_SkipsWhitespaceClaims()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "  "),
            new Claim("oid", ""),
            new Claim("sub", "real-sub"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("real-sub", result!.ActorId);
    }

    [Fact]
    public void Resolve_NameIdentifierBeatsOidAndSub()
    {
        var resolver = CreateResolver();
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "nameid-wins"),
            new Claim("oid", "oid-loses"),
            new Claim("sub", "sub-loses"),
            new Claim(ClaimTypes.Name, "name-loses"),
            new Claim("preferred_username", "pref-loses"));

        var result = resolver.Resolve(CreateHttpContext(principal));

        Assert.NotNull(result);
        Assert.Equal("nameid-wins", result!.ActorId);
    }

    // ── Header fallback ─────────────────────────────────────────────

    [Fact]
    public void Resolve_ReturnsHeader_WhenNoClaimsAndHeaderFallbackEnabled()
    {
        var resolver = CreateResolver(allowHeaderFallback: true);
        var ctx = CreateHttpContext(actorHeader: "header-actor");

        var result = resolver.Resolve(ctx);

        Assert.NotNull(result);
        Assert.Equal("header-actor", result!.ActorId);
        Assert.Equal("header", result.Source);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_IgnoresHeader_WhenHeaderFallbackDisabled()
    {
        var resolver = CreateResolver(allowHeaderFallback: false);
        var ctx = CreateHttpContext(actorHeader: "header-actor");

        var result = resolver.Resolve(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_IgnoresEmptyHeader_WhenHeaderFallbackEnabled()
    {
        var resolver = CreateResolver(allowHeaderFallback: true);
        var ctx = CreateHttpContext(actorHeader: "  ");

        var result = resolver.Resolve(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ClaimsWinOverHeader_WhenBothPresent()
    {
        var resolver = CreateResolver(allowHeaderFallback: true);
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "claims-win"));
        var ctx = CreateHttpContext(principal, actorHeader: "header-loses");

        var result = resolver.Resolve(ctx);

        Assert.NotNull(result);
        Assert.Equal("claims-win", result!.ActorId);
        Assert.Equal("claim", result.Source);
        Assert.True(result.IsAuthenticated);
    }

    // ── Anonymous fallback ──────────────────────────────────────────

    [Fact]
    public void Resolve_ReturnsUnknown_WhenAnonymousFallbackEnabled()
    {
        var resolver = CreateResolver(allowAnonymousFallback: true);
        var ctx = CreateHttpContext();

        var result = resolver.Resolve(ctx);

        Assert.NotNull(result);
        Assert.Equal("unknown", result!.ActorId);
        Assert.Equal("anonymous", result.Source);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenAllFallbacksDisabled()
    {
        var resolver = CreateResolver(
            allowHeaderFallback: false,
            allowAnonymousFallback: false);
        var ctx = CreateHttpContext();

        var result = resolver.Resolve(ctx);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_HeaderBeatsAnonymous_WhenBothEnabled()
    {
        var resolver = CreateResolver(
            allowHeaderFallback: true,
            allowAnonymousFallback: true);
        var ctx = CreateHttpContext(actorHeader: "header-actor");

        var result = resolver.Resolve(ctx);

        Assert.NotNull(result);
        Assert.Equal("header-actor", result!.ActorId);
        Assert.Equal("header", result.Source);
    }

    [Fact]
    public void Resolve_FallsToAnonymous_WhenHeaderEmptyAndBothEnabled()
    {
        var resolver = CreateResolver(
            allowHeaderFallback: true,
            allowAnonymousFallback: true);
        var ctx = CreateHttpContext(actorHeader: "");

        var result = resolver.Resolve(ctx);

        Assert.NotNull(result);
        Assert.Equal("unknown", result!.ActorId);
        Assert.Equal("anonymous", result.Source);
    }

    // ── Unauthenticated principal with claims should not resolve ─────

    [Fact]
    public void Resolve_IgnoresClaimsFromUnauthenticatedPrincipal()
    {
        var resolver = CreateResolver();
        // Unauthenticated identity — claims are present but identity is not authenticated
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "should-be-ignored") });
        var principal = new ClaimsPrincipal(identity);
        var ctx = CreateHttpContext(principal);

        var result = resolver.Resolve(ctx);

        Assert.Null(result);
    }

    // ── Config defaults to false ────────────────────────────────────

    [Fact]
    public void Resolve_FallbacksDefaultToFalse_WhenConfigMissing()
    {
        var config = new ConfigurationBuilder().Build();
        var resolver = new ClaimsActorIdentityResolver(config);
        var ctx = CreateHttpContext(actorHeader: "should-be-ignored");

        var result = resolver.Resolve(ctx);

        Assert.Null(result);
    }
}
