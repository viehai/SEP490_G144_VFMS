using VFMS.Application.Interfaces;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// Core authentication use-case service. Implements login, register, refresh, and logout.
/// GBR-26, GBR-27, GBR-28.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IAuthRepository _authRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IAuthRepository authRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetUserByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result<LoginResponse>.Failure("Invalid email or password.", "AUTH_INVALID_CREDENTIALS", 401);

        if (user.AccountStatus == AccountStatus.Locked)
            return Result<LoginResponse>.Failure("Account is locked. Please contact support.", "AUTH_ACCOUNT_LOCKED", 403);

        if (user.AccountStatus == AccountStatus.Inactive)
            return Result<LoginResponse>.Failure("Account is inactive.", "AUTH_ACCOUNT_INACTIVE", 403);

        if (user.AccountStatus == AccountStatus.PendingVerification)
            return Result<LoginResponse>.Failure("Account is pending verification.", "AUTH_PENDING_VERIFICATION", 403);

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var accessToken = _jwtTokenService.GenerateAccessToken(user, roles);
        var refreshToken = await _jwtTokenService.GenerateRefreshTokenAsync(user.UserId, request.RememberMe, ipAddress, cancellationToken);

        await _authRepository.UpdateLastLoginAsync(user.UserId, cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenType: "Bearer",
            ExpiresIn: 1800,
            UserId: user.UserId,
            FullName: user.FullName,
            Email: user.Email,
            Roles: roles
        ));
    }

    public async Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (await _authRepository.EmailExistsAsync(request.Email, cancellationToken))
            return Result<RegisterResponse>.Failure("Email is already in use.", "AUTH_EMAIL_EXISTS", 409);

        var customerRole = await _authRepository.GetRoleByNameAsync("Customer", cancellationToken);
        if (customerRole is null)
            return Result<RegisterResponse>.Failure("Default role not configured.", "AUTH_ROLE_NOT_FOUND", 500);

        var defaultGym = await _authRepository.GetDefaultGymAsync(cancellationToken);
        if (defaultGym is null)
            return Result<RegisterResponse>.Failure("No gym configured in the system.", "AUTH_NO_GYM", 500);

        var newUser = new ApplicationUser
        {
            UserId = Guid.NewGuid().ToString(),
            GymId = defaultGym.Id,
            FullName = request.FullName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.Hash(request.Password),
            AccountStatus = AccountStatus.Active
        };

        await _authRepository.CreateUserAsync(newUser, customerRole.Id, cancellationToken);

        return Result<RegisterResponse>.Success(new RegisterResponse(
            UserId: newUser.UserId,
            FullName: newUser.FullName,
            Email: newUser.Email,
            Message: "Registration successful. You can now log in."
        ), 201);
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _jwtTokenService.GetActiveRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (refreshToken is null)
            return Result<LoginResponse>.Failure("Invalid or expired refresh token.", "AUTH_INVALID_REFRESH_TOKEN", 401);

        var user = refreshToken.User;
        if (user.AccountStatus != AccountStatus.Active)
            return Result<LoginResponse>.Failure("Account is not active.", "AUTH_ACCOUNT_NOT_ACTIVE", 403);

        // Revoke old token and issue new pair (token rotation)
        await _jwtTokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var newAccessToken = _jwtTokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = await _jwtTokenService.GenerateRefreshTokenAsync(user.UserId, refreshToken.RememberMe, ipAddress, cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            AccessToken: newAccessToken,
            RefreshToken: newRefreshToken,
            TokenType: "Bearer",
            ExpiresIn: 1800,
            UserId: user.UserId,
            FullName: user.FullName,
            Email: user.Email,
            Roles: roles
        ));
    }

    public async Task<Result<bool>> LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        await _jwtTokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return Result<bool>.Success(true);
    }
}
