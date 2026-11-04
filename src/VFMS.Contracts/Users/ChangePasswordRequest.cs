namespace VFMS.Contracts.Users;

public sealed record ChangePasswordRequest(
    string OldPassword,
    string NewPassword
);
