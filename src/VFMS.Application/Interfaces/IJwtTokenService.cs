using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

/// <summary>
/// Generates and validates JWT access tokens and refresh tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Generates a 30-minute JWT access token for the user.</summary>
    string GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    /// <summary>Generates a refresh token (7d or 30d with Remember Me) and persists it to DB.</summary>
    Task<string> GenerateRefreshTokenAsync(string userId, bool rememberMe, string? createdByIp, CancellationToken cancellationToken = default);

    /// <summary>Validates a refresh token and returns the associated user ID if valid.</summary>
    Task<RefreshToken?> GetActiveRefreshTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Revokes a refresh token (blacklists it immediately – GBR-28).</summary>
    Task RevokeRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
}
