namespace SampleMobile.Application.Common;

/// <summary>One page of results plus the total count across all pages.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount) {
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Shared paging bounds so every list endpoint clamps the same way.</summary>
public static class Paging {
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static IReadOnlyList<OperationError> Validate(int page, int pageSize) {
        var errors = new List<OperationError>();
        if (page < 1) {
            errors.Add(new OperationError("page", "Page must be 1 or greater."));
        }

        if (pageSize is < 1 or > MaxPageSize) {
            errors.Add(new OperationError("pageSize", $"Page size must be between 1 and {MaxPageSize}."));
        }

        return errors;
    }
}
