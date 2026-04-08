using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 149 (JWT Bearer) + Slice 202 (OIDC browser login).
///
/// Registers three authentication schemes that coexist:
///
///   SmartBearer (policy scheme, default)
///     ├─ If Authorization: Bearer … header → route to JwtBearer
///     └─ Otherwise                         → route to Cookie
///
///   Cookie — browser session set after successful OIDC login
///     LoginPath  = /account/login  (no-JS redirect to Microsoft)
///     SaveTokens = true so Blazor pages can read the access token
///
///   JwtBearer — for programmatic API clients (CI/CD, curl, integrations)
///     Validates Entra ID tokens with audience check
///
///   OpenIdConnect — handles the Entra ID redirect dance
///     Scope includes access_as_user so the access token covers the API
///
/// Config:
///   Authentication:Entra:TenantId      — Entra ID tenant GUID
///   Authentication:Entra:Audience      — App registration audience URI (api://…)
///   Authentication:Entra:ClientId      — App registration client ID (same as App ID)
///   Authentication:Entra:ClientSecret  — App registration client secret (from Key Vault)
///   Authentication:Cookie:Name         — Cookie name (default: "OcSession")
///   Authentication:Cookie:ExpireMinutes— Sliding session length (default: 480 = 8 h)
///   Authentication:DevBypass           — true = synthetic dev-user, no Entra required
/// </summary>
internal static class AuthenticationExtensions
{
    internal static IServiceCollection AddOpsCopilotAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var devBypass = configuration.GetValue<bool>("Authentication:DevBypass");

        if (devBypass)
        {
            // ── Development: synthetic user, no Entra required ────────────────
            services.AddAuthentication(DevBypassDefaults.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevBypassAuthHandler>(
                    DevBypassDefaults.SchemeName, _ => { });
        }
        else
        {
            // ── Production: OIDC + Cookie + JWT Bearer ────────────────────────
            var tenantId     = Require(configuration, "Authentication:Entra:TenantId");
            var audience     = Require(configuration, "Authentication:Entra:Audience");
            var clientId     = Require(configuration, "Authentication:Entra:ClientId");
            var clientSecret = Require(configuration, "Authentication:Entra:ClientSecret");

            var cookieName    = configuration["Authentication:Cookie:Name"] ?? "OcSession";
            var expireMinutes = configuration.GetValue<int>("Authentication:Cookie:ExpireMinutes", 480);
            var authority     = $"https://login.microsoftonline.com/{tenantId}/v2.0";

            services.AddAuthentication(options =>
            {
                // SmartBearer routes to Cookie or JwtBearer based on request headers
                options.DefaultScheme          = SmartBearerDefaults.SchemeName;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                options.DefaultSignInScheme    = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme(SmartBearerDefaults.SchemeName, "Smart Bearer", opts =>
            {
                opts.ForwardDefaultSelector = ctx =>
                {
                    // Programmatic API client with Bearer token → use JwtBearer
                    var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
                    if (auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;

                    // Browser session → use Cookie
                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
            {
                opts.LoginPath        = "/account/login";
                opts.AccessDeniedPath = "/account/accessdenied";
                opts.SlidingExpiration = true;
                opts.ExpireTimeSpan   = TimeSpan.FromMinutes(expireMinutes);
                opts.Cookie.HttpOnly  = true;
                opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax is required for the OIDC POST-redirect callback to send the cookie
                opts.Cookie.SameSite  = SameSiteMode.Lax;
                opts.Cookie.Name      = cookieName;
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, opts =>
            {
                opts.Authority     = authority;
                opts.ClientId      = clientId;
                opts.ClientSecret  = clientSecret;
                opts.ResponseType  = OpenIdConnectResponseType.Code;   // PKCE code flow
                opts.SaveTokens    = true;   // stores access_token in the cookie session

                // Request profile + access to our own API so the access_token is usable
                opts.Scope.Clear();
                opts.Scope.Add("openid");
                opts.Scope.Add("profile");
                opts.Scope.Add("email");
                opts.Scope.Add($"{audience}/access_as_user");

                opts.GetClaimsFromUserInfoEndpoint = true;
                opts.CallbackPath         = "/signin-oidc";
                opts.SignedOutCallbackPath = "/signout-callback-oidc";
                opts.RemoteSignOutPath    = "/signout-oidc";

                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "roles"
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.Authority = authority;
                opts.Audience  = audience;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer   = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ClockSkew        = TimeSpan.FromMinutes(5)
                };
            });
        }

        // Fallback policy: every endpoint requires an authenticated user unless it
        // explicitly calls .AllowAnonymous(). Health probes + login routes use .AllowAnonymous().
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

        return services;
    }

    private static string Require(IConfiguration cfg, string key)
    {
        var value = cfg[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be set when Authentication:DevBypass is false.");
        return value;
    }
}

internal static class SmartBearerDefaults
{
    internal const string SchemeName = "SmartBearer";
}

internal static class DevBypassDefaults
{
    internal const string SchemeName = "DevBypass";
}

/// <summary>
/// Development-only authentication handler that always succeeds.
/// Presents a synthetic "dev-user" principal so local development
/// works without an Entra token. Never active when DevBypass=false.
/// </summary>
internal sealed class DevBypassAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevBypassAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name,           "Dev User"),
            new Claim(ClaimTypes.NameIdentifier, "dev-user"),
            new Claim(ClaimTypes.Email,          "dev@localhost"),
            new Claim("name",                    "Dev User")
        };
        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
