using Microsoft.AspNetCore.Diagnostics;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 163 — RFC 7807 ProblemDetails registration and global exception handler.
///
/// AddProblemDetails() enables IProblemDetailsService so that:
///   • Results.Problem() in Minimal API handlers emits Content-Type: application/problem+json
///   • app.UseExceptionHandler() catches unhandled exceptions → 500 ProblemDetails
///   • app.UseStatusCodePages() maps 401/403/404 → ProblemDetails (no bare HTTP status)
///
/// In Development, full exception detail (type + message) is included in extension
/// fields to aid local debugging.  In all other environments it is suppressed to
/// prevent information leakage in production log streams or API responses.
/// </summary>
internal static class ErrorHandlingExtensions
{
    internal static IServiceCollection AddOpsCopilotErrorHandling(
        this IServiceCollection services,
        bool includeDevelopmentDetail)
    {
        services.AddProblemDetails(options =>
        {
            if (!includeDevelopmentDetail)
                return;

            // Development only: surface exception type and message in the
            // response body.  Never enabled outside Development because the
            // IHostEnvironment flag is evaluated at service-registration time.
            options.CustomizeProblemDetails = ctx =>
            {
                if (ctx.HttpContext.Features.Get<IExceptionHandlerFeature>() is { Error: var ex })
                {
                    ctx.ProblemDetails.Extensions["exceptionType"] = ex.GetType().FullName;
                    ctx.ProblemDetails.Extensions["detail"]        = ex.Message;
                }
            };
        });

        return services;
    }
}
