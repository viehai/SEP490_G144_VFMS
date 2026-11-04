namespace VFMS.Contracts.Auth;

public sealed record LogoutRequest(
    string RefreshToken
);
