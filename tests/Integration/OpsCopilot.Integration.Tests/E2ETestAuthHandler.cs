using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// Shared auth handler for E2E integration tests.
/// Always authenticates with a deterministic test identity so that
/// endpoints guarded by <c>IActorIdentityResolver</c> see a valid principal.
/// </summary>
public sealed class E2ETestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "E2ETest";
    public const string TestActorId = "e2e-test-user";
    public const string TestActorName = "E2E Test User";

    public E2ETestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestActorId),
            new Claim(ClaimTypes.Name, TestActorName),
        };
        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
