using VFMS.Domain.Common;

namespace VFMS.Application.Interfaces;

/// <summary>
/// Minimal unit-of-work used by services that write an entity, its audit log row and notifications together.
/// </summary>
public interface IAuditedRepository
{
    void Add<TEntity>(TEntity entity) where TEntity : BaseEntity;
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
    /// <summary>IDs of active (not locked/inactive) users holding the role, used as notification recipients.</summary>
    Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default);
}
