using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

/// <summary>
/// Repository interface for Auth-related data access (implemented in Infrastructure).
/// </summary>
public interface IAuthRepository
{
    Task<ApplicationUser?> GetUserByEmailOrPhoneAsync(string emailOrPhone, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default);
    Task CreateUserAsync(ApplicationUser user, int roleId, CancellationToken cancellationToken = default);
    Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default);
}
