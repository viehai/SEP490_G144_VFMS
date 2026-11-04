using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class UserAdministrationRepository : IUserAdministrationRepository
{
    private readonly VfmsDbContext _context;

    public UserAdministrationRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers.AnyAsync(user => user.Email == email, cancellationToken);

    public Task<bool> PhoneNumberExistsAsync(
        string phoneNumber,
        string? excludingUserId = null,
        CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers.AnyAsync(
            user => user.PhoneNumber == phoneNumber &&
                (excludingUserId == null || user.UserId != excludingUserId),
            cancellationToken);

    public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
        _context.Gyms.FirstOrDefaultAsync(cancellationToken);

    public Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
        _context.ApplicationRoles.FirstOrDefaultAsync(
            role => role.RoleName == roleName, cancellationToken);

    public async Task<IReadOnlyList<ApplicationRole>> GetRolesByNamesAsync(
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        var normalizedNames = roleNames.ToArray();
        return await _context.ApplicationRoles
            .Where(role => normalizedNames.Contains(role.RoleName))
            .ToListAsync(cancellationToken);
    }

    public Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
        string? role,
        AccountStatus? accountStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ApplicationUsers
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(user => user.UserRoles.Any(userRole => userRole.Role.RoleName == role));

        if (accountStatus.HasValue)
            query = query.Where(user => user.AccountStatus == accountStatus.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.FullName.Contains(search) ||
                user.Email.Contains(search) ||
                user.PhoneNumber.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (users, totalCount);
    }

    public async Task CreateUserAsync(
        ApplicationUser user,
        int roleId,
        AuditLog? auditLog = null,
        CancellationToken cancellationToken = default)
    {
        _context.ApplicationUsers.Add(user);
        _context.ApplicationUserRoles.Add(new ApplicationUserRole
        {
            UserId = user.UserId,
            RoleId = roleId
        });

        if (auditLog is not null)
            _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceUserRolesAsync(
        ApplicationUser user,
        IReadOnlyCollection<int> roleIds,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        _context.ApplicationUserRoles.RemoveRange(user.UserRoles);
        foreach (var roleId in roleIds)
        {
            _context.ApplicationUserRoles.Add(new ApplicationUserRole
            {
                UserId = user.UserId,
                RoleId = roleId
            });
        }

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAccountStatusAsync(
        ApplicationUser user,
        AccountStatus status,
        bool revokeActiveRefreshTokens,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        user.AccountStatus = status;
        if (revokeActiveRefreshTokens)
        {
            var activeTokens = await _context.RefreshTokens
                .Where(token => token.UserId == user.UserId &&
                    token.RevokedAt == null &&
                    token.ExpiresAt > DateTime.UtcNow)
                .ToListAsync(cancellationToken);

            var revokedAt = DateTime.UtcNow;
            foreach (var token in activeTokens)
                token.RevokedAt = revokedAt;
        }

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
