using Clinic.SharedKernel.Errors;
using Microsoft.AspNetCore.Antiforgery;

namespace Clinic.Api.Composition;

public sealed class SecurityOptions
{
    /// <summary>Extra origins allowed on unsafe methods (development SPA dev server only). Production: none.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Security headers and no-store on every API response (SECURITY_DESIGN.md §3, §12).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        headers.CacheControl = "no-store";
        return next(context);
    }
}

/// <summary>
/// CSRF on every unsafe method (SECURITY_DESIGN.md §3): Origin and Sec-Fetch-Site must be same-origin,
/// and the anti-forgery token must be present in X-CSRF-TOKEN. The public feedback routes use token binding.
/// </summary>
internal sealed class CsrfMiddleware(RequestDelegate next, IAntiforgery antiforgery,
    Microsoft.Extensions.Options.IOptions<SecurityOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsProtected(context.Request))
        {
            if (!IsSameOrigin(context.Request))
            {
                await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.CsrfInvalid);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.CsrfInvalid);
                return;
            }
        }

        await next(context);
    }

    private static bool IsProtected(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
        && request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments("/api/public");

    private bool IsSameOrigin(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none")
            && !(fetchSite == "same-site" && OriginAllowed(request)))
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0 || OriginAllowed(request);
    }

    private bool OriginAllowed(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return true;
        }

        var self = $"{request.Scheme}://{request.Host}";
        return string.Equals(origin, self, StringComparison.OrdinalIgnoreCase)
               || options.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Unmatched routes are 404 for everyone (the fallback deny policy applies to mapped endpoints).</summary>
internal sealed class UnmatchedRouteMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) =>
        context.GetEndpoint() is null
            ? Problems.WriteAsync(context, StatusCodes.Status404NotFound, ErrorCodes.NotFound)
            : next(context);
}
