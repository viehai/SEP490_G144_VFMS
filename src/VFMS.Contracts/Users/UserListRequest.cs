namespace VFMS.Contracts.Users;

public sealed record UserListRequest(
    string? Role = null,
    string? AccountStatus = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);
