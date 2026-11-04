namespace VFMS.Contracts.Users;

public sealed record UpdateUserProfileRequest(
    string FullName,
    string PhoneNumber,
    string? AvatarUrl = null
);
