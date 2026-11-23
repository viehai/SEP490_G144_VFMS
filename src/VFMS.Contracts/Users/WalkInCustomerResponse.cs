namespace VFMS.Contracts.Users;

public sealed record WalkInCustomerResponse(
    string UserId,
    string FullName,
    string Email,
    string PhoneNumber,
    string AccountStatus,
    IReadOnlyCollection<string> Roles,
    DateTime? PasswordSetupExpiresAt,
    bool SetupEmailSent,
    string Message
);

public sealed record ResendSetupEmailResponse(
    string UserId,
    string Email,
    DateTime PasswordSetupExpiresAt,
    string Message
);
