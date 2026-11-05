using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class SupplementRepository : AuditedRepositoryBase, ISupplementRepository
{
    public SupplementRepository(VfmsDbContext context) : base(context) { }

    // ── Products ─────────────────────────────────────────────────

    public async Task<(IReadOnlyList<SupplementProduct> Items, int TotalCount)> SearchProductsAsync(
        IReadOnlyCollection<SupplementProductStatus> statuses, string? category, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.SupplementProducts.AsNoTracking().Where(p => statuses.Contains(p.ProductStatus));
        if (category is not null)
            query = query.Where(p => p.CatalogCategory == category);
        if (search is not null)
            query = query.Where(p => p.ProductName.Contains(search) ||
                                     (p.CatalogCategory != null && p.CatalogCategory.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.ProductName).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<SupplementProduct?> GetProductAsync(int id, CancellationToken cancellationToken = default) =>
        Context.SupplementProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    // SQL Server's default collation is case-insensitive, so this also catches "WHEY" vs "whey".
    public Task<bool> ProductNameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default) =>
        Context.SupplementProducts.AnyAsync(p => p.ProductName == name && (excludeId == null || p.Id != excludeId), cancellationToken);

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(
        IReadOnlyCollection<SupplementProductStatus> statuses, CancellationToken cancellationToken = default) =>
        await Context.SupplementProducts
            .Where(p => statuses.Contains(p.ProductStatus) && p.CatalogCategory != null)
            .Select(p => p.CatalogCategory!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplementProduct>> GetProductsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        await Context.SupplementProducts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    // ── Orders ───────────────────────────────────────────────────

    public async Task<(IReadOnlyList<SupplementOrder> Items, int TotalCount)> SearchOrdersAsync(
        string? customerUserId, IReadOnlyCollection<SupplementOrderStatus>? statuses, SupplementDeliveryType? deliveryType,
        DateTime? fromUtc, DateTime? toUtcExclusive, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.SupplementOrders.AsNoTracking().AsQueryable();
        if (customerUserId is not null)
            query = query.Where(o => o.CustomerUserId == customerUserId);
        if (statuses is not null)
            query = query.Where(o => statuses.Contains(o.OrderStatus));
        if (deliveryType.HasValue)
            query = query.Where(o => o.DeliveryType == deliveryType);
        if (fromUtc.HasValue)
            query = query.Where(o => o.CreatedAt >= fromUtc);
        if (toUtcExclusive.HasValue)
            query = query.Where(o => o.CreatedAt < toUtcExclusive);
        if (search is not null)
            query = query.Where(o => o.OrderNumber.Contains(search) || o.Customer.FullName.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var items = await WithDetails(query)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<SupplementOrder?> GetOrderAsync(int id, CancellationToken cancellationToken = default) =>
        WithDetails(Context.SupplementOrders).AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        Context.SupplementOrders.IgnoreQueryFilters().AnyAsync(o => o.OrderNumber == orderNumber, cancellationToken);

    // Products stay visible on old orders even after being hidden, so no status filter on the item's product.
    private static IQueryable<SupplementOrder> WithDetails(IQueryable<SupplementOrder> query) =>
        query
            .Include(o => o.Customer)
            .Include(o => o.Delivery)
            .Include(o => o.Items).ThenInclude(i => i.SupplementProduct);
}
