using System.Diagnostics;
using System.Text.Json;
using Clinic.SharedKernel.Errors;

namespace Clinic.Api.Composition;

/// <summary>
/// RFC 7807 problem+json writer (FR-OPS-006, API A6): type, title, status, stable code, traceId and field
/// codes. Generic titles only; never input values, SQL or stack traces.
/// </summary>
internal static class Problems
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int status, string code,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";

        var body = new Dictionary<string, object?>
        {
            ["type"] = $"https://clinic.local/problems/{code}",
            ["title"] = TitleFor(status),
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
        };
        if (fieldErrors is { Count: > 0 })
        {
            body["errors"] = fieldErrors;
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.MalformedRequest,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        _ => ErrorCodes.InternalError,
    };

    private static string TitleFor(int status) => status switch
    {
        400 => "Bad request",
        401 => "Unauthenticated",
        403 => "Forbidden",
        404 => "Not found",
        405 => "Method not allowed",
        409 => "Conflict",
        412 => "Precondition failed",
        422 => "Unprocessable request",
        428 => "Precondition required",
        429 => "Too many requests",
        _ => "Server error",
    };
}
