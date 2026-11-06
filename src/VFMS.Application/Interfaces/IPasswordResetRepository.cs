using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IPasswordResetRepository
{
    Task<ApplicationUser?> FindEligibleUserAsync(string email, CancellationToken cancellationToken);
    Task<bool> TryIssueAsync(string userId, string expectedPasswordHash, string tokenHash, DateTime expiresAt, DateTime cooldownCutoff, DateTime now, CancellationToken cancellationToken);
    Task<ApplicationUser?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken);
    Task InvalidateAsync(string userId, string tokenHash, DateTime now, CancellationToken cancellationToken);
    Task<bool> TryResetAsync(string userId, string tokenHash, string expectedPasswordHash, string newPasswordHash, DateTime now, CancellationToken cancellationToken);
}
