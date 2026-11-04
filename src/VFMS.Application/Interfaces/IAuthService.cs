using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;

namespace VFMS.Application.Interfaces;

/// <summary>
/// Core authentication use-case service (Module 1).
/// </summary>
public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<bool>> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default);
}
