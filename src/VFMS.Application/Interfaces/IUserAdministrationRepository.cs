using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IUserAdministrationRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> PhoneNumberExistsAsync(
        string phoneNumber,
        string? excludingUserId = null,
        CancellationToken cancellationToken = default);
    Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default);
    Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationRole>> GetRolesByNamesAsync(
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
        string? role,
        AccountStatus? accountStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task CreateUserAsync(
        ApplicationUser user,
        int roleId,
        AuditLog? auditLog = null,
        CancellationToken cancellationToken = default);
    Task ReplaceUserRolesAsync(
        ApplicationUser user,
        IReadOnlyCollection<int> roleIds,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task UpdateAccountStatusAsync(
        ApplicationUser user,
        AccountStatus status,
        bool revokeActiveRefreshTokens,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
