namespace VehicleRental.Application;

/// <summary>
/// The paging limits shared by every list use case.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Checks a page request and converts it to the number of items to skip.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is outside the allowed range.</exception>
    internal static int ToSkip(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        long skip = (long)(page - 1) * pageSize;

        if (skip > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "The page number is too large.");
        }

        return (int)skip;
    }
}

/// <summary>
/// One page of results returned by a use case.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
