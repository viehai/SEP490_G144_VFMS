namespace VFMS.Contracts.Users;

public sealed record UserProfileResponse(
    string UserId,
    string FullName,
    string? Email,
    string PhoneNumber,
    string? AvatarUrl,
    string AccountStatus,
    IReadOnlyCollection<string> Roles
);
