using Clinic.SharedKernel.Errors;
using Microsoft.AspNetCore.Http;

namespace Clinic.SharedKernel.Persistence;

/// <summary>Entity carrying the PostgreSQL xmin row version (ETag / If-Match, FR-OPS-005).</summary>
public interface IRowVersioned
{
    uint RowVersion { get; }
}

public static class ETags
{
    public static string Format(uint rowVersion) => $"\"{rowVersion}\"";

    public static void Set(HttpResponse response, uint rowVersion) =>
        response.Headers.ETag = Format(rowVersion);

    /// <summary>428 when If-Match is missing, 412 when it does not match the current version.</summary>
    public static void Require(HttpRequest request, uint currentRowVersion) =>
        Require(request, Format(currentRowVersion));

    public static void Require(HttpRequest request, string currentETag)
    {
        var ifMatch = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw AppException.PreconditionRequired();
        }

        if (!string.Equals(ifMatch.Trim(), currentETag, StringComparison.Ordinal))
        {
            throw AppException.Stale();
        }
    }
}
