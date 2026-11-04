namespace VFMS.Contracts.Users;

public sealed record CreateStaffRequest(
    string FullName,
    string Email,
    string PhoneNumber,
    string Password,
    string ConfirmPassword,
    string RoleName
);
