using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class SupplementRepository : AuditedRepositoryBase, ISupplementRepository
{
    public SupplementRepository(VfmsDbContext context) : base(context) { }

    // ── Products ─────────────────────────────────────────────────

    public async Task<(IReadOnlyList<SupplementProduct> Items, int TotalCount)> SearchProductsAsync(
        SupplementProductQuery filter, CancellationToken cancellationToken = default)
    {
        var statuses = filter.Statuses;
        var query = Context.SupplementProducts.AsNoTracking().Where(p => statuses.Contains(p.ProductStatus));
        if (filter.Category is { } category)
            query = query.Where(p => p.CatalogCategory == category);
        if (filter.Brand is { } brand)
            query = query.Where(p => p.Brand == brand);
        if (filter.MinPrice is { } min)
            query = query.Where(p => p.UnitPrice >= min);
        if (filter.MaxPrice is { } max)
            query = query.Where(p => p.UnitPrice <= max);
        if (filter.LowStockOnly)
            query = query.Where(p => p.StockQuantity != null && p.StockQuantity <= SupplementStock.LowStockThreshold);
        if (filter.Search is { } search)
            query = query.Where(p => p.ProductName.Contains(search) ||
                                     (p.Brand != null && p.Brand.Contains(search)) ||
                                     (p.CatalogCategory != null && p.CatalogCategory.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await Order(query, filter)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Applies the requested order. When <see cref="SupplementProductQuery.AvailableFirst"/> is set,
    /// products that can be bought come before OutOfStock ones whatever the sort.
    /// The product id is the final tie-break so pages never overlap.
    /// </summary>
    private static IQueryable<SupplementProduct> Order(IQueryable<SupplementProduct> query, SupplementProductQuery filter)
    {
        var ordered = filter.AvailableFirst
            ? query.OrderBy(p => p.ProductStatus == SupplementProductStatus.OutOfStock ? 1 : 0)
            : query.OrderBy(p => 0);
        ordered = filter.Sort switch
        {
            SupplementProductSort.PriceAscending => ordered.ThenBy(p => p.UnitPrice).ThenBy(p => p.ProductName),
            SupplementProductSort.PriceDescending => ordered.ThenByDescending(p => p.UnitPrice).ThenBy(p => p.ProductName),
            SupplementProductSort.Newest => ordered.ThenByDescending(p => p.CreatedAt),
            _ => ordered.ThenBy(p => p.ProductName),
        };
        return ordered.ThenBy(p => p.Id);
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

    public async Task<IReadOnlyList<string>> GetBrandsAsync(
        IReadOnlyCollection<SupplementProductStatus> statuses, CancellationToken cancellationToken = default) =>
        await Context.SupplementProducts
            .Where(p => statuses.Contains(p.ProductStatus) && p.Brand != null)
            .Select(p => p.Brand!)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplementProduct>> GetProductsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        await Context.SupplementProducts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<SupplementProduct?> GetProductSnapshotAsync(int id, CancellationToken cancellationToken = default) =>
        Context.SupplementProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    // ── Stock ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StockShortage>> ReserveStockAsync(
        IReadOnlyCollection<(int ProductId, int Quantity)> lines, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var shortages = new List<StockShortage>();
        foreach (var (productId, quantity) in lines)
        {
            // Only succeeds while enough units are left, so concurrent orders cannot take the same units twice.
            var taken = await Context.SupplementProducts
                .Where(p => p.Id == productId && p.StockQuantity != null && p.StockQuantity >= quantity)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
            if (taken > 0) continue;
            var product = await Context.SupplementProducts.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => new { p.ProductName, p.StockQuantity })
                .FirstOrDefaultAsync(cancellationToken);
            if (product?.StockQuantity is { } available) // tracked and short; untracked products need no stock
                shortages.Add(new StockShortage(productId, product.ProductName, available));
        }
        if (shortages.Count == 0)
            await SyncStatusAsync(lines.Select(l => l.ProductId).ToArray(), now, cancellationToken);
        return shortages;
    }

    public async Task ReleaseStockAsync(
        IReadOnlyCollection<(int ProductId, int Quantity)> lines, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var (productId, quantity) in lines)
            await Context.SupplementProducts
                .Where(p => p.Id == productId && p.StockQuantity != null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
        await SyncStatusAsync(lines.Select(l => l.ProductId).ToArray(), now, cancellationToken);
    }

    public async Task<(int? Before, int? After)> AdjustStockAsync(
        int productId, SupplementStockAction action, int quantity, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var before = await Context.SupplementProducts.AsNoTracking()
            .Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync(cancellationToken);
        var products = Context.SupplementProducts.Where(p => p.Id == productId);
        switch (action)
        {
            case SupplementStockAction.Add:
                await products.ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StockQuantity, p => (p.StockQuantity ?? 0) + quantity)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
                break;
            case SupplementStockAction.Set:
                await products.ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StockQuantity, quantity)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
                break;
            default:
                await products.ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.StockQuantity, (int?)null)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
                break;
        }
        await SyncStatusAsync([productId], now, cancellationToken);
        var after = await Context.SupplementProducts.AsNoTracking()
            .Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync(cancellationToken);
        return (before, after);
    }

    /// <summary>A tracked product is OutOfStock at 0 and Active again once it has stock; Inactive is never changed.</summary>
    private async Task SyncStatusAsync(IReadOnlyCollection<int> productIds, DateTime now, CancellationToken cancellationToken)
    {
        await Context.SupplementProducts
            .Where(p => productIds.Contains(p.Id) && p.StockQuantity == 0 && p.ProductStatus == SupplementProductStatus.Active)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.ProductStatus, SupplementProductStatus.OutOfStock)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);
        await Context.SupplementProducts
            .Where(p => productIds.Contains(p.Id) && p.StockQuantity > 0 && p.ProductStatus == SupplementProductStatus.OutOfStock)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.ProductStatus, SupplementProductStatus.Active)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);
    }

    // ── Orders ───────────────────────────────────────────────────

    public async Task<IReadOnlyDictionary<SupplementOrderStatus, int>> CountOrdersByStatusAsync(CancellationToken cancellationToken = default) =>
        await Context.SupplementOrders.AsNoTracking()
            .GroupBy(o => o.OrderStatus)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

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
