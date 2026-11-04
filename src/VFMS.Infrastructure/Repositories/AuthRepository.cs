using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
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

    public async Task<ApplicationUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.ApplicationUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.ApplicationUsers.AnyAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        return await _context.ApplicationRoles.FirstOrDefaultAsync(r => r.RoleName == roleName, cancellationToken);
    }

    public async Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Gyms.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task CreateUserAsync(ApplicationUser user, int roleId, CancellationToken cancellationToken = default)
    {
        _context.ApplicationUsers.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        _context.ApplicationUserRoles.Add(new ApplicationUserRole
        {
            UserId = user.UserId,
            RoleId = roleId
        });
        await _context.SaveChangesAsync(cancellationToken);
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
