using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class UserAdministrationRepository : IUserAdministrationRepository
{
    private static readonly SemaphoreSlim NonSqlServerProvisioningGate = new(1, 1);

    private readonly VfmsDbContext _context;
    private readonly TimeProvider _clock;

    public UserAdministrationRepository(VfmsDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
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

    public Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Include(user => user.PhysicalProfile)
            .Include(user => user.TrainerProfile).ThenInclude(profile => profile!.TrainerRole)
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public Task<ApplicationUser?> GetUserByPasswordSetupTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .FirstOrDefaultAsync(user => user.PasswordSetupTokenHash == tokenHash, cancellationToken);

    public Task<ApplicationUser?> FindPendingCustomerAsync(
        string? canonicalEmail,
        string? canonicalPhone,
        CancellationToken cancellationToken = default) =>
        EligiblePendingCustomers().AsNoTracking().SingleOrDefaultAsync(
            user => (canonicalEmail == null || user.Email == canonicalEmail) &&
                (canonicalPhone == null || user.PhoneNumber == canonicalPhone),
            cancellationToken);

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

    public async Task<Result<T>> ExecuteProvisioningAsync<T>(
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken = default)
    {
        var isSqlServer = _context.Database.IsSqlServer();
        var gateAcquired = false;
        if (!isSqlServer)
        {
            await NonSqlServerProvisioningGate.WaitAsync(cancellationToken);
            gateAcquired = true;
        }

        try
        {
            // The canonical contact indexes arbitrate conflicts across all account writers.
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await action();
                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                }

                return result;
            }
            catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) == 1205)
            {
                // SQL Server has already rolled back the deadlock victim.
                _context.ChangeTracker.Clear();
                return Result<T>.Failure("Account provisioning is busy. Please retry shortly.", "USER_PROVISIONING_CONFLICT", 409);
            }
            catch (DbUpdateException exception) when (AccountIdentityConflict.GetField(exception) is { } field)
            {
                await RollbackAfterDatabaseFailureAsync(transaction);
                _context.ChangeTracker.Clear();
                return AccountIdentityConflict.ResultFor<T>(field);
            }
        }
        finally
        {
            if (gateAcquired)
                NonSqlServerProvisioningGate.Release();
        }
    }

    public async Task CreateUserAsync(
        ApplicationUser user,
        AuditLog? auditLog = null,
        CancellationToken cancellationToken = default)
    {
        _context.ApplicationUsers.Add(user);

        if (auditLog is not null)
            _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<T>> ExecuteCustomerSetupResendAsync<T>(
        string userId,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken = default)
    {
        var isSqlServer = _context.Database.IsSqlServer();
        if (!isSqlServer && !await NonSqlServerProvisioningGate.WaitAsync(0, cancellationToken))
            return ResendBusy<T>();

        try
        {
            // Keep the previously committed token usable unless the replacement email is accepted.
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (isSqlServer)
                    await AcquireCustomerSetupLockAsync(userId, 0, cancellationToken);

                var result = await action();
                if (result.IsSuccess)
                    await transaction.CommitAsync(CancellationToken.None);
                else
                    await transaction.RollbackAsync(CancellationToken.None);
                return result;
            }
            catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) is int error &&
                error is 51001 or 1205)
            {
                if (error == 51001)
                    await RollbackAfterDatabaseFailureAsync(transaction);
                return ResendBusy<T>();
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
        finally
        {
            if (!isSqlServer)
                NonSqlServerProvisioningGate.Release();
        }
    }

    public async Task<bool> TryRotateCustomerSetupTokenAsync(
        string userId,
        string expectedEmail,
        string tokenHash,
        DateTime expiresAt,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var updated = await EligiblePendingCustomers()
            .Where(user => user.UserId == userId && user.Email == expectedEmail)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.PasswordSetupTokenHash, tokenHash)
                .SetProperty(user => user.PasswordSetupExpiresAt, expiresAt)
                .SetProperty(user => user.UpdatedAt, now),
                cancellationToken);

        return updated == 1;
    }

    public async Task ClearPasswordSetupTokenAsync(
        string userId,
        string tokenHash,
        DateTime now,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.ApplicationUsers
            .Where(user => user.UserId == userId && user.PasswordSetupTokenHash == tokenHash)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.PasswordSetupTokenHash, (string?)null)
                .SetProperty(user => user.PasswordSetupExpiresAt, (DateTime?)null)
                .SetProperty(user => user.UpdatedAt, now),
                cancellationToken);
        await SaveSetupAuditAsync([auditLog], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveSetupAuditAsync(
        IReadOnlyCollection<AuditLog> auditLogs,
        CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.AddRange(auditLogs);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryCompletePasswordSetupAsync(
        string userId,
        string tokenHash,
        string newPasswordHash,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        var isSqlServer = _context.Database.IsSqlServer();
        if (!isSqlServer)
            await NonSqlServerProvisioningGate.WaitAsync(cancellationToken);

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (isSqlServer)
                    await AcquireCustomerSetupLockAsync(userId, 10_000, cancellationToken);

                var now = _clock.GetUtcNow().UtcDateTime;
                var updated = await EligiblePendingCustomers()
                    .Where(user => user.UserId == userId &&
                        user.PasswordSetupTokenHash == tokenHash && user.PasswordSetupExpiresAt > now)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(user => user.PasswordHash, newPasswordHash)
                        .SetProperty(user => user.PasswordSetupTokenHash, (string?)null)
                        .SetProperty(user => user.PasswordSetupExpiresAt, (DateTime?)null)
                        .SetProperty(user => user.ResetTokenHash, (string?)null)
                        .SetProperty(user => user.ResetTokenExpiresAt, (DateTime?)null)
                        .SetProperty(user => user.AccountStatus, AccountStatus.Active)
                        .SetProperty(user => user.UpdatedAt, now),
                        cancellationToken);

                if (updated != 1)
                    return false;

                auditLog.OccurredAt = now;
                await SaveSetupAuditAsync([auditLog], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) is int error &&
                error is 51001 or 1205)
            {
                if (error == 51001)
                    await RollbackAfterDatabaseFailureAsync(transaction);
                _context.ChangeTracker.Clear();
                return false;
            }
        }
        finally
        {
            if (!isSqlServer)
                NonSqlServerProvisioningGate.Release();
        }
    }

    private IQueryable<ApplicationUser> EligiblePendingCustomers() => _context.ApplicationUsers.Where(user =>
        user.AccountStatus == AccountStatus.PendingVerification && !user.IsBanned &&
        user.UserRoles.Count(assignment => !assignment.IsDeleted && !assignment.Role.IsDeleted) == 1 &&
        user.UserRoles.Any(assignment => !assignment.IsDeleted && !assignment.Role.IsDeleted &&
            assignment.Role.RoleName == "Customer"));

    private Task AcquireCustomerSetupLockAsync(string userId, int timeout, CancellationToken cancellationToken)
    {
        var resource = "VFMS.CustomerSetup:" + userId.Trim().ToUpperInvariant();
        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DECLARE @result int;
             EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                 @LockOwner='Transaction', @LockTimeout={timeout};
             IF @result < 0 THROW 51001, 'Customer setup is busy', 1;
             """, cancellationToken);
    }

    private static Result<T> ResendBusy<T>() => Result<T>.Failure(
        "A setup operation is already in progress for this Customer. Please try again shortly.",
        "CUSTOMER_SETUP_RESEND_BUSY", 409);

    public Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken) =>
        _context.YogaClasses.AnyAsync(c => !c.IsDeleted && c.InstructorUserId == userId &&
            (c.ClassStatus == YogaClassStatus.Draft || c.ClassStatus == YogaClassStatus.Published), cancellationToken);

    public async Task<bool> SetUserRoleAsync(
        ApplicationUser user,
        int roleId,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _context.ApplicationUsers
            .Where(u => u.UserId == user.UserId && u.AccessVersion == user.AccessVersion)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccessVersion, u => u.AccessVersion + 1), cancellationToken);
        if (updated == 0)
            return false;

        _context.ApplicationUserRoles.RemoveRange(user.UserRoles.Where(assignment => assignment.RoleId != roleId).ToArray());
        if (!user.UserRoles.Any(assignment => assignment.RoleId == roleId))
        {
            var previous = await _context.ApplicationUserRoles.IgnoreQueryFilters()
                .SingleOrDefaultAsync(assignment => assignment.UserId == user.UserId && assignment.RoleId == roleId, cancellationToken);
            if (previous is null)
            {
                _context.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = user.UserId, RoleId = roleId });
            }
            else
            {
                previous.IsDeleted = false;
            }
        }

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var version = _context.Entry(user).Property(u => u.AccessVersion);
        version.OriginalValue = version.CurrentValue = user.AccessVersion + 1;
        return true;
    }

    public async Task<AccountStatus?> GetStatusBeforeLatestLockAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var oldValue = await _context.AuditLogs
            .Where(log => log.AuditEntity == $"ApplicationUser:{userId}" && log.ActionType == AuditActionType.AccountLock)
            .OrderByDescending(log => log.OccurredAt)
            .ThenByDescending(log => log.Id)
            .Select(log => log.OldValue)
            .FirstOrDefaultAsync(cancellationToken);

        return oldValue switch
        {
            nameof(AccountStatus.Active) => AccountStatus.Active,
            nameof(AccountStatus.Inactive) => AccountStatus.Inactive,
            nameof(AccountStatus.PendingVerification) => AccountStatus.PendingVerification,
            _ => null
        };
    }

    public async Task<bool> TryUpdateAccountStatusAsync(
        ApplicationUser user,
        AccountStatus status,
        string? lockReason,
        bool revokeActiveRefreshTokens,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        var previousStatus = status == AccountStatus.Locked ? user.AccountStatus : (AccountStatus?)null;
        var now = auditLog.OccurredAt;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _context.ApplicationUsers
            .Where(candidate => candidate.UserId == user.UserId && candidate.AccountStatus == user.AccountStatus &&
                candidate.AccountStatusBeforeLock == user.AccountStatusBeforeLock && candidate.AccessVersion == user.AccessVersion)
            .ExecuteUpdateAsync(update => update
                .SetProperty(candidate => candidate.AccountStatus, status)
                .SetProperty(candidate => candidate.AccountStatusBeforeLock, previousStatus)
                .SetProperty(candidate => candidate.LockReason, lockReason)
                .SetProperty(candidate => candidate.AccessVersion, candidate => candidate.AccessVersion + 1)
                .SetProperty(candidate => candidate.UpdatedAt, now), cancellationToken);
        if (updated != 1)
            return false;

        if (revokeActiveRefreshTokens)
        {
            await _context.RefreshTokens
                .Where(token => token.UserId == user.UserId &&
                    token.RevokedAt == null &&
                    token.ExpiresAt > now)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(token => token.RevokedAt, (DateTime?)now)
                    .SetProperty(token => token.UpdatedAt, now), cancellationToken);
        }

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var entry = _context.Entry(user);
        entry.Property(candidate => candidate.LockReason).CurrentValue = lockReason;
        entry.Property(candidate => candidate.LockReason).OriginalValue = lockReason;
        entry.Property(candidate => candidate.AccessVersion).CurrentValue++;
        entry.Property(candidate => candidate.AccessVersion).OriginalValue = user.AccessVersion;
        entry.Property(candidate => candidate.AccountStatus).CurrentValue = status;
        entry.Property(candidate => candidate.AccountStatus).OriginalValue = status;
        entry.Property(candidate => candidate.AccountStatusBeforeLock).CurrentValue = previousStatus;
        entry.Property(candidate => candidate.AccountStatusBeforeLock).OriginalValue = previousStatus;
        entry.Property(candidate => candidate.UpdatedAt).CurrentValue = now;
        entry.Property(candidate => candidate.UpdatedAt).OriginalValue = now;
        return true;
    }

    public async Task<Result<bool>> UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (DbUpdateException exception) when (AccountIdentityConflict.GetField(exception) is { } field)
        {
            _context.ChangeTracker.Clear();
            return AccountIdentityConflict.ResultFor<bool>(field);
        }
        catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) == 1205)
        {
            _context.ChangeTracker.Clear();
            return AccountIdentityConflict.WriteConflict<bool>();
        }
    }

    private static async Task RollbackAfterDatabaseFailureAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or SqlException)
        {
            // A deadlock victim or broken connection can leave no transaction to roll back.
        }
    }
}
