namespace VFMS.Contracts.Auth;

public sealed record SetupPasswordRequest(
    string Token,
    string NewPassword,
    string ConfirmPassword
);

public sealed record SetupPasswordResponse(string Message);
