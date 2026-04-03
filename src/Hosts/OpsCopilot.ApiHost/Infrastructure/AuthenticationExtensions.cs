using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 149 — Registers Entra ID JWT bearer authentication and a fallback
/// authorization policy that requires authenticated users on all endpoints.
/// Health probes opt out via .AllowAnonymous() in HealthCheckExtensions.
///
/// Config:
///   Authentication:Entra:TenantId  — Entra ID tenant GUID (required when DevBypass=false)
///   Authentication:Entra:Audience  — App registration audience URI (required when DevBypass=false)
///   Authentication:DevBypass       — true = accept any caller; local development only
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
            services.AddAuthentication(DevBypassDefaults.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevBypassAuthHandler>(
                    DevBypassDefaults.SchemeName, _ => { });
        }
        else
        {
            var tenantId = configuration["Authentication:Entra:TenantId"];
            var audience = configuration["Authentication:Entra:Audience"];

            if (string.IsNullOrWhiteSpace(tenantId))
                throw new InvalidOperationException(
                    "Authentication:Entra:TenantId must be configured when Authentication:DevBypass is false.");

            if (string.IsNullOrWhiteSpace(audience))
                throw new InvalidOperationException(
                    "Authentication:Entra:Audience must be configured when Authentication:DevBypass is false.");

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                    options.Audience = audience;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(5)
                    };
                });
        }

        // Fallback policy: all endpoints require an authenticated user unless
        // they explicitly call .AllowAnonymous(). Health probes use .AllowAnonymous().
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

        return services;
    }
}

internal static class DevBypassDefaults
{
    internal const string SchemeName = "DevBypass";
}

/// <summary>
/// Development-only authentication handler that always succeeds.
/// Presents a synthetic "dev-user" principal. Never active in production
/// because Authentication:DevBypass defaults to false.
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
        var claims = new[] { new Claim(ClaimTypes.Name, "dev-user") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
