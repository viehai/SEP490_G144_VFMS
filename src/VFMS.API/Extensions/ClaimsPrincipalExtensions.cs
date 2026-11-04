using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace VFMS.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's ID (JWT "sub"), or null when absent.</summary>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }
}
