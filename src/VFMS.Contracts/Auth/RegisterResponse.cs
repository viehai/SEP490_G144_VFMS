namespace VFMS.Contracts.Auth;

public sealed record RegisterResponse(
    string UserId,
    string FullName,
    string Email,
    string Message
);
