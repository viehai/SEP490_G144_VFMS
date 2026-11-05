using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

/// <summary>Shared unit-of-work plumbing for repositories that implement <see cref="IAuditedRepository"/>.</summary>
public abstract class AuditedRepositoryBase : IAuditedRepository
{
    protected readonly VfmsDbContext Context;

    protected AuditedRepositoryBase(VfmsDbContext context)
    {
        Context = context;
    }

    // Non-generic Add resolves the entity's runtime type, so callers holding a BaseEntity reference work too.
    public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => Context.Add((object)entity);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Context.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        if (Context.Database.CurrentTransaction is not null)
        {
            await work();
            return;
        }
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await work();
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
        await Context.ApplicationUserRoles
            .Where(ur => ur.Role.RoleName == roleName && ur.User.AccountStatus == AccountStatus.Active)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
        Context.ApplicationUserRoles.AnyAsync(ur => ur.UserId == userId && ur.Role.RoleName == roleName, cancellationToken);
}

public sealed class EquipmentRepository : AuditedRepositoryBase, IEquipmentRepository
{
    private static readonly EquipmentIssueStatus[] OpenStatuses =
        [EquipmentIssueStatus.Reported, EquipmentIssueStatus.InProgress, EquipmentIssueStatus.Escalated];

    public EquipmentRepository(VfmsDbContext context) : base(context) { }

    // ── Categories ───────────────────────────────────────────────

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await Context.CatalogCategories.AsNoTracking().OrderBy(c => c.CategoryName).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, int>> CountEquipmentByCategoryAsync(CancellationToken cancellationToken = default) =>
        await Context.Equipments
            .Where(e => e.CatalogCategoryId != null)
            .GroupBy(e => e.CatalogCategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public Task<CatalogCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        Context.CatalogCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> CategoryNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.CatalogCategories.AnyAsync(
            c => c.CategoryName.ToLower() == name.ToLower() && (excludingId == null || c.Id != excludingId), cancellationToken);

    public Task<bool> CategoryInUseAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Equipments.AnyAsync(e => e.CatalogCategoryId == id, cancellationToken);

    // ── Equipment ────────────────────────────────────────────────

    public async Task<(IReadOnlyList<Equipment> Items, int TotalCount)> SearchEquipmentAsync(
        string? search, int? categoryId, EquipmentStatus? status, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Equipments.AsNoTracking().Include(e => e.CatalogCategory).AsQueryable();
        if (search is not null)
            query = query.Where(e => e.EquipmentName.Contains(search) || (e.EquipmentCode != null && e.EquipmentCode.Contains(search)));
        if (categoryId.HasValue)
            query = query.Where(e => e.CatalogCategoryId == categoryId);
        if (status.HasValue)
            query = query.Where(e => e.EquipmentStatus == status);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(e => e.EquipmentName).ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Equipment?> GetEquipmentAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Equipments.Include(e => e.CatalogCategory).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> EquipmentCodeExistsAsync(string code, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.Equipments.AnyAsync(
            e => e.EquipmentCode != null && e.EquipmentCode.ToLower() == code.ToLower() &&
                 (excludingId == null || e.Id != excludingId), cancellationToken);

    public async Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default) =>
        await Context.Gyms.OrderBy(g => g.Id).Select(g => (int?)g.Id).FirstOrDefaultAsync(cancellationToken);

    // ── Issues ───────────────────────────────────────────────────

    public Task<bool> HasOpenIssueAsync(int equipmentId, int? excludingIssueId, CancellationToken cancellationToken = default) =>
        Context.EquipmentIssueReports.AnyAsync(
            i => i.EquipmentId == equipmentId && OpenStatuses.Contains(i.IssueStatus) &&
                 (excludingIssueId == null || i.Id != excludingIssueId), cancellationToken);

    public async Task<(IReadOnlyList<EquipmentIssueReport> Items, int TotalCount)> SearchIssuesAsync(
        EquipmentIssueStatus? status, int? equipmentId, string? reportedByUserId, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = IssuesWithDetails().AsNoTracking();
        if (status.HasValue)
            query = query.Where(i => i.IssueStatus == status);
        if (equipmentId.HasValue)
            query = query.Where(i => i.EquipmentId == equipmentId);
        if (reportedByUserId is not null)
            query = query.Where(i => i.ReportedByUserId == reportedByUserId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.ReportedAt).ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<EquipmentIssueReport?> GetIssueAsync(int id, CancellationToken cancellationToken = default) =>
        IssuesWithDetails().AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    private IQueryable<EquipmentIssueReport> IssuesWithDetails() =>
        Context.EquipmentIssueReports
            .Include(i => i.Equipment)
            .Include(i => i.ReportedByUser)
            .Include(i => i.HandledByUser)
            .Include(i => i.SafetyConfirmations).ThenInclude(c => c.ConfirmedByUser);
}
