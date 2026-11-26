using VFMS.Contracts.Common;
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
    Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserByPasswordSetupTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);
    Task<ApplicationUser?> FindPendingCustomerAsync(
        string? canonicalEmail,
        string? canonicalPhone,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
        string? role,
        AccountStatus? accountStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<Result<T>> ExecuteProvisioningAsync<T>(
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken = default);
    Task CreateUserAsync(
        ApplicationUser user,
        AuditLog? auditLog = null,
        CancellationToken cancellationToken = default);
    Task<Result<T>> ExecuteCustomerSetupResendAsync<T>(
        string userId,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken = default);
    Task<bool> TryRotateCustomerSetupTokenAsync(
        string userId,
        string expectedEmail,
        string tokenHash,
        DateTime expiresAt,
        DateTime now,
        CancellationToken cancellationToken = default);
    Task ClearPasswordSetupTokenAsync(
        string userId,
        string tokenHash,
        DateTime now,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task SaveSetupAuditAsync(
        IReadOnlyCollection<AuditLog> auditLogs,
        CancellationToken cancellationToken = default);
    Task<bool> TryCompletePasswordSetupAsync(
        string userId,
        string tokenHash,
        string newPasswordHash,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken);
    Task<bool> SetUserRoleAsync(
        ApplicationUser user,
        int roleId,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task<AccountStatus?> GetStatusBeforeLatestLockAsync(
        string userId,
        CancellationToken cancellationToken = default);
    Task<bool> TryUpdateAccountStatusAsync(
        ApplicationUser user,
        AccountStatus status,
        string? lockReason,
        bool revokeActiveRefreshTokens,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
    Task<Result<bool>> UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
