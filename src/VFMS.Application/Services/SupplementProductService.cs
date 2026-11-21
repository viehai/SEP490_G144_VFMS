using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-77 View Supplement Products (Customer, Member) and UC-83 Manage Supplement Products (Manager).
/// GBR-69: stock is tracked when a product has a stock quantity; a tracked product is Active while it has stock and
/// OutOfStock at 0, an untracked product is set OutOfStock by hand. GBR-70: Inactive products are hidden from the store.
/// The store only sees the stock quantity when it is low. Products are never deleted; hiding one means setting it Inactive.
/// </summary>
public sealed class SupplementProductService : ISupplementProductService
{
    private const string Entity = nameof(SupplementProduct);

    internal static readonly SupplementProductStatus[] StoreStatuses =
        [SupplementProductStatus.Active, SupplementProductStatus.OutOfStock];
    private static readonly SupplementProductStatus[] AllStatuses = Enum.GetValues<SupplementProductStatus>();

    private readonly ISupplementRepository _repository;
    private readonly IMapper _mapper;

    public SupplementProductService(ISupplementRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public Task<Result<PagedResponse<SupplementProductResponse>>> GetStoreProductsAsync(
        SupplementProductListRequest request, CancellationToken cancellationToken = default) =>
        SearchAsync(request, storeView: true, cancellationToken);

    public Task<Result<PagedResponse<SupplementProductResponse>>> GetAllProductsAsync(
        SupplementProductListRequest request, CancellationToken cancellationToken = default) =>
        SearchAsync(request, storeView: false, cancellationToken);

    public async Task<Result<SupplementProductResponse>> GetProductAsync(
        int id, bool storeView, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetProductAsync(id, cancellationToken);
        if (product is null || storeView && !StoreStatuses.Contains(product.ProductStatus))
            return NotFound();
        return Result<SupplementProductResponse>.Success(ToResponse(product, storeView));
    }

    public async Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(bool storeView, CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<string>>.Success(
            await _repository.GetCategoriesAsync(storeView ? StoreStatuses : AllStatuses, cancellationToken));

    public async Task<Result<IReadOnlyList<string>>> GetBrandsAsync(bool storeView, CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<string>>.Success(
            await _repository.GetBrandsAsync(storeView ? StoreStatuses : AllStatuses, cancellationToken));

    public async Task<Result<SupplementProductResponse>> CreateAsync(
        SupplementProductRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = CleanName(request.Name);
        if (await _repository.ProductNameExistsAsync(name, null, cancellationToken))
            return NameExists();
        if (!EquipmentService.TryParse(request.Status, out SupplementProductStatus status))
            return InvalidStatus();

        var product = new SupplementProduct { StockQuantity = request.StockQuantity };
        if (product.StockQuantity == 0 && status == SupplementProductStatus.Active)
            status = SupplementProductStatus.OutOfStock; // a tracked product with nothing in stock cannot be sold
        Apply(product, request, name, status);
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Create, Entity, ipAddress, newValue: Describe(product));
        await AuditedWriter.SaveNewAsync(_repository, product, audit, null, cancellationToken);
        return Result<SupplementProductResponse>.Success(_mapper.Map<SupplementProductResponse>(product), 201);
    }

    /// <summary>Existing orders keep the unit price they were placed with (UC-83 AF1).</summary>
    public async Task<Result<SupplementProductResponse>> UpdateAsync(
        int id, SupplementProductRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound();
        var name = CleanName(request.Name);
        if (await _repository.ProductNameExistsAsync(name, id, cancellationToken))
            return NameExists();
        if (!EquipmentService.TryParse(request.Status, out SupplementProductStatus status))
            return InvalidStatus();
        if (status == SupplementProductStatus.Active && product.StockQuantity == 0)
            return NoStock();

        var before = Describe(product);
        Apply(product, request, name, status);
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(product));
        await AuditedWriter.SaveChangedAsync(_repository, product, audit, null, cancellationToken);
        return Result<SupplementProductResponse>.Success(_mapper.Map<SupplementProductResponse>(product));
    }

    public async Task<Result<SupplementProductResponse>> ChangeStatusAsync(
        int id, string status, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!EquipmentService.TryParse(status, out SupplementProductStatus newStatus))
            return InvalidStatus();
        var product = await _repository.GetProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound();
        if (product.ProductStatus == newStatus)
            return Result<SupplementProductResponse>.Failure(
                $"The product is already {newStatus}.", "SUPPLEMENT_PRODUCT_STATUS_UNCHANGED", 409);

        if (newStatus == SupplementProductStatus.Active && product.StockQuantity == 0)
            return NoStock();
        var before = product.ProductStatus;
        product.ProductStatus = newStatus;
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={before}", $"Status={newStatus}");
        await AuditedWriter.SaveChangedAsync(_repository, product, audit, null, cancellationToken);
        return Result<SupplementProductResponse>.Success(_mapper.Map<SupplementProductResponse>(product));
    }

    /// <summary>
    /// Restock (Add), stock count (Set) or stop tracking. The change is applied in the database in one step so an
    /// order placed at the same moment is never lost; Active/OutOfStock follows the new stock (Inactive stays).
    /// </summary>
    public async Task<Result<SupplementProductResponse>> AdjustStockAsync(
        int id, SupplementStockRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!EquipmentService.TryParse(request.Action, out SupplementStockAction action))
            return Result<SupplementProductResponse>.Failure("Action must be Add, Set or StopTracking.", "SUPPLEMENT_STOCK_INVALID_ACTION");
        var quantity = action == SupplementStockAction.StopTracking ? 0 : request.Quantity;
        var (min, max) = action == SupplementStockAction.Add ? (1, SupplementStock.MaxQuantity) : (0, SupplementStock.MaxQuantity);
        if (quantity < min || quantity > max)
            return Result<SupplementProductResponse>.Failure(
                $"Quantity must be between {min} and {max}.", "SUPPLEMENT_STOCK_INVALID_QUANTITY");
        if (await _repository.GetProductSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        SupplementProduct? after = null;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            var (before, now) = await _repository.AdjustStockAsync(id, action, quantity, cancellationToken);
            var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
                $"Stock={Show(before)}", $"Stock={Show(now)}; Action={action}" + (EquipmentService.Clean(request.Note) is { } note ? $"; Note={note}" : ""));
            audit.EntityId = id;
            _repository.Add(audit);
            await _repository.SaveChangesAsync(cancellationToken);
            after = await _repository.GetProductSnapshotAsync(id, cancellationToken);
        }, cancellationToken);
        return Result<SupplementProductResponse>.Success(ToResponse(after!, storeView: false));
    }

    private static string Show(int? stock) => stock?.ToString() ?? "not tracked";

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>The store sees the stock quantity only when it is low ("Chỉ còn 3"); the Manager always sees it.</summary>
    private SupplementProductResponse ToResponse(SupplementProduct product, bool storeView)
    {
        var response = _mapper.Map<SupplementProductResponse>(product);
        return storeView && !response.LowStock ? response with { StockQuantity = null } : response;
    }

    private async Task<Result<PagedResponse<SupplementProductResponse>>> SearchAsync(
        SupplementProductListRequest request, bool storeView, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SupplementProductStatus> statuses = storeView ? StoreStatuses : AllStatuses;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out SupplementProductStatus parsed))
                return Result<PagedResponse<SupplementProductResponse>>.Failure(
                    "Status filter is invalid.", "SUPPLEMENT_PRODUCT_INVALID_STATUS");
            // The store can narrow to Active or OutOfStock, but never reach Inactive products.
            statuses = statuses.Contains(parsed) ? [parsed] : [];
        }

        var query = new SupplementProductQuery(
            statuses,
            EquipmentService.Clean(request.Category),
            EquipmentService.Clean(request.Search),
            EquipmentService.Clean(request.Brand),
            request.MinPrice,
            request.MaxPrice,
            ParseSort(request.Sort),
            AvailableFirst: storeView,
            request.Page,
            request.PageSize,
            LowStockOnly: !storeView && request.LowStock);
        var (items, total) = await _repository.SearchProductsAsync(query, cancellationToken);
        return Result<PagedResponse<SupplementProductResponse>>.Success(new PagedResponse<SupplementProductResponse>(
            request.Page, request.PageSize, total, items.Select(p => ToResponse(p, storeView)).ToArray()));
    }

    private static void Apply(SupplementProduct product, SupplementProductRequest request, string name, SupplementProductStatus status)
    {
        product.ProductName = name;
        product.Description = request.Description.Trim();
        product.CatalogCategory = EquipmentService.Clean(request.Category);
        product.Brand = EquipmentService.Clean(request.Brand);
        product.Size = EquipmentService.Clean(request.Size);
        product.Flavour = EquipmentService.Clean(request.Flavour);
        product.ImageUrl = request.ImageUrl.Trim();
        product.UnitPrice = request.UnitPrice;
        product.ProductStatus = status;
    }

    /// <summary>Maps the API sort value; anything blank or unknown sorts by name.</summary>
    private static SupplementProductSort ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "price_asc" => SupplementProductSort.PriceAscending,
        "price_desc" => SupplementProductSort.PriceDescending,
        "newest" => SupplementProductSort.Newest,
        _ => SupplementProductSort.Name,
    };

    private static string CleanName(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Describe(SupplementProduct p) =>
        $"Name={p.ProductName}; Brand={p.Brand}; Category={p.CatalogCategory}; Size={p.Size}; Flavour={p.Flavour}; Price={p.UnitPrice}; Status={p.ProductStatus}; Stock={Show(p.StockQuantity)}";

    private static Result<SupplementProductResponse> NotFound() =>
        Result<SupplementProductResponse>.Failure("Supplement product was not found.", "SUPPLEMENT_PRODUCT_NOT_FOUND", 404);

    private static Result<SupplementProductResponse> NameExists() =>
        Result<SupplementProductResponse>.Failure(
            "A product with this name already exists.", "SUPPLEMENT_PRODUCT_NAME_EXISTS", 409);

    private static Result<SupplementProductResponse> NoStock() =>
        Result<SupplementProductResponse>.Failure(
            "The product has no stock left. Add stock before setting it Active.", "SUPPLEMENT_PRODUCT_NO_STOCK", 409);

    private static Result<SupplementProductResponse> InvalidStatus() =>
        Result<SupplementProductResponse>.Failure(
            "Status must be Active, Inactive or OutOfStock.", "SUPPLEMENT_PRODUCT_INVALID_STATUS");
}
