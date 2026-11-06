using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class PasswordResetRepository(VfmsDbContext context) : IPasswordResetRepository
{
    public Task<ApplicationUser?> FindEligibleUserAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        return context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(
            user => user.Email.ToUpper() == normalized && user.AccountStatus == AccountStatus.Active && !user.IsBanned, cancellationToken);
    }

    public async Task<bool> TryIssueAsync(string userId, string expectedPasswordHash, string tokenHash,
        DateTime expiresAt, DateTime cooldownCutoff, DateTime now, CancellationToken cancellationToken) =>
        await context.ApplicationUsers.Where(user => user.UserId == userId && user.PasswordHash == expectedPasswordHash &&
            user.AccountStatus == AccountStatus.Active && !user.IsBanned &&
            (user.ResetTokenExpiresAt == null || user.ResetTokenExpiresAt <= cooldownCutoff))
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.ResetTokenHash, tokenHash)
                .SetProperty(user => user.ResetTokenExpiresAt, expiresAt).SetProperty(user => user.UpdatedAt, now), cancellationToken) == 1;

    public Task<ApplicationUser?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(user => user.ResetTokenHash == tokenHash, cancellationToken);

    public async Task InvalidateAsync(string userId, string tokenHash, DateTime now, CancellationToken cancellationToken) =>
        await context.ApplicationUsers.Where(user => user.UserId == userId && user.ResetTokenHash == tokenHash)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.ResetTokenHash, (string?)null)
                .SetProperty(user => user.ResetTokenExpiresAt, (DateTime?)null).SetProperty(user => user.UpdatedAt, now), cancellationToken);

    public async Task<bool> TryResetAsync(string userId, string tokenHash, string expectedPasswordHash, string newPasswordHash,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Exactly one request may consume the current token and password snapshot.
        var changed = await context.ApplicationUsers.Where(user => user.UserId == userId &&
            user.ResetTokenHash == tokenHash && user.ResetTokenExpiresAt > now && user.PasswordHash == expectedPasswordHash &&
            user.AccountStatus == AccountStatus.Active && !user.IsBanned)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.PasswordHash, newPasswordHash)
                .SetProperty(user => user.ResetTokenHash, (string?)null)
                .SetProperty(user => user.ResetTokenExpiresAt, (DateTime?)null)
                .SetProperty(user => user.UpdatedAt, now), cancellationToken);
        if (changed != 1) return false;

        await context.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, now).SetProperty(token => token.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
