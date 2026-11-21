using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of IAuthRepository.
/// </summary>
public sealed class AuthRepository : IAuthRepository
{
    private readonly VfmsDbContext _context;

    public AuthRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public async Task<ApplicationUser?> GetUserByEmailOrPhoneAsync(
        string emailOrPhone,
        CancellationToken cancellationToken = default)
    {
        var users = _context.ApplicationUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role);

        var identifier = UserIdentityRules.NormalizeEmail(emailOrPhone);
        var matches = UserIdentityRules.TryNormalizePhone(emailOrPhone, out var phoneNumber)
            ? users.Where(user => user.PhoneNumber == phoneNumber)
            : users.Where(user => user.Email == identifier);

        var candidates = await matches.Take(2).ToListAsync(cancellationToken);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.ApplicationUsers.AnyAsync(u => u.Email == email, cancellationToken);
    }

    public Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);

    public async Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        return await _context.ApplicationRoles.FirstOrDefaultAsync(r => r.RoleName == roleName, cancellationToken);
    }

    public async Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Gyms.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<bool>> CreateUserAsync(ApplicationUser user, int roleId, CancellationToken cancellationToken = default)
    {
        _context.ApplicationUsers.Add(user);
        _context.ApplicationUserRoles.Add(new ApplicationUserRole
        {
            UserId = user.UserId,
            RoleId = roleId
        });
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

    public async Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.ApplicationUsers
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is not null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
