namespace Clinic.SharedKernel.Paging;

/// <summary>List response shape (API A5): { items, page, pageSize, totalCount }.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class LikePattern
{
    /// <summary>Escapes LIKE wildcards so user input is matched literally (SECURITY_DESIGN.md §13).</summary>
    public static string Escape(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
