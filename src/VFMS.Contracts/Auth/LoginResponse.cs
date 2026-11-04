namespace VFMS.Contracts.Auth;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    string UserId,
    string FullName,
    string Email,
    IEnumerable<string> Roles
);
