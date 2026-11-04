namespace VFMS.Contracts.Common;

/// <summary>
/// Generic page of results returned by list endpoints.
/// </summary>
public sealed record PagedResponse<T>(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<T> Items
);
