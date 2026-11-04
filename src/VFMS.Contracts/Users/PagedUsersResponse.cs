namespace VFMS.Contracts.Users;

public sealed record PagedUsersResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<UserAccountResponse> Items
);
