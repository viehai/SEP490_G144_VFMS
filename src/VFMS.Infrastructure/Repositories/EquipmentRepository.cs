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

    /// <summary>Exclusive SQL Server app lock held until the current transaction ends (check-then-insert races).</summary>
    protected async Task AppLockAsync(string resource, string busyMessage, CancellationToken cancellationToken)
    {
        if (!Context.Database.IsSqlServer())
            return;   // SQLite test databases have no app locks and run one request at a time.
        var result = await Context.Database.SqlQuery<int>(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r AS [Value]")
            .ToListAsync(cancellationToken);
        if (result.Count == 0 || result[0] < 0)
            throw new TimeoutException(busyMessage);
    }
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

    public Task<bool> CategoryPrefixExistsAsync(string prefix, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.CatalogCategories.AnyAsync(
            c => c.CodePrefix != null && c.CodePrefix == prefix && (excludingId == null || c.Id != excludingId), cancellationToken);

    public Task<bool> CategoryInUseAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Equipments.AnyAsync(e => e.CatalogCategoryId == id, cancellationToken);

    // ── Equipment ────────────────────────────────────────────────

    public async Task<(IReadOnlyList<Equipment> Items, int TotalCount)> SearchEquipmentAsync(
        EquipmentQuery filter, CancellationToken cancellationToken = default)
    {
        var query = Context.Equipments.AsNoTracking().Include(e => e.CatalogCategory).AsQueryable();
        if (filter.Search is { } search)
            query = query.Where(e => e.EquipmentName.Contains(search) || (e.EquipmentCode != null && e.EquipmentCode.Contains(search)));
        if (filter.CategoryId.HasValue)
            query = query.Where(e => e.CatalogCategoryId == filter.CategoryId);
        if (filter.Status.HasValue)
            query = query.Where(e => e.EquipmentStatus == filter.Status);
        if (filter.Brand is { } brand)
            query = query.Where(e => e.EquipmentBrand == brand);

        var total = await query.CountAsync(cancellationToken);
        IOrderedQueryable<Equipment> ordered = filter.Sort switch
        {
            EquipmentSort.Code => query.OrderBy(e => e.EquipmentCode == null).ThenBy(e => e.EquipmentCode).ThenBy(e => e.EquipmentName),
            EquipmentSort.Newest => query.OrderByDescending(e => e.CreatedAt),
            EquipmentSort.PurchaseDate => query.OrderBy(e => e.PurchaseDate == null).ThenByDescending(e => e.PurchaseDate).ThenBy(e => e.EquipmentName),
            _ => query.OrderBy(e => e.EquipmentName),
        };
        var items = await ordered.ThenBy(e => e.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default) =>
        await Context.Equipments
            .Where(e => e.EquipmentBrand != null && e.EquipmentBrand != "")
            .Select(e => e.EquipmentBrand!)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync(cancellationToken);

    public Task<Equipment?> GetEquipmentAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Equipments.Include(e => e.CatalogCategory).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<int> GetMaxCodeNumberAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var start = prefix + "-";
        var codes = await Context.Equipments.IgnoreQueryFilters()
            .Where(e => e.EquipmentCode != null && e.EquipmentCode.StartsWith(start))
            .Select(e => e.EquipmentCode!)
            .ToListAsync(cancellationToken);
        return codes
            .Select(code => int.TryParse(code[start.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

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
        EquipmentIssueQuery filter, CancellationToken cancellationToken = default)
    {
        var query = IssuesWithDetails().AsNoTracking();
        if (filter.Status.HasValue)
            query = query.Where(i => i.IssueStatus == filter.Status);
        if (filter.EquipmentId.HasValue)
            query = query.Where(i => i.EquipmentId == filter.EquipmentId);
        if (filter.ReportedByUserId is { } reporter)
            query = query.Where(i => i.ReportedByUserId == reporter);
        if (filter.Search is { } search)
            query = query.Where(i => i.Equipment.EquipmentName.Contains(search) ||
                                     (i.Equipment.EquipmentCode != null && i.Equipment.EquipmentCode.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var ordered = filter.Sort == EquipmentIssueSort.Severity
            ? query.OrderBy(i => i.IncidentType == IncidentType.Safety ? 0
                               : i.IncidentType == IncidentType.Damage ? 1
                               : i.IncidentType == IncidentType.Malfunction ? 2 : 3)
                   .ThenByDescending(i => i.ReportedAt)
            : query.OrderByDescending(i => i.ReportedAt);
        var items = await ordered.ThenByDescending(i => i.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
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
