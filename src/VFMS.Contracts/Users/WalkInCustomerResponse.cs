namespace VFMS.Contracts.Users;

public sealed record WalkInCustomerResponse(
    string UserId,
    string FullName,
    string? Email,
    string PhoneNumber,
    string AccountStatus,
    IReadOnlyCollection<string> Roles,
    string GeneratedPassword
);
