namespace VFMS.Contracts.Users;

public sealed record UserAccountResponse(
    string UserId,
    string FullName,
    string? Email,
    string PhoneNumber,
    string AccountStatus,
    IReadOnlyCollection<string> Roles
);
