using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-77 View Supplement Products (Customer, Member) and UC-83 Manage Supplement Products (Manager).
/// GBR-69: no stock tracking, OutOfStock is set manually. GBR-70: Inactive products are hidden from the store.
/// Products are never deleted; hiding a product means setting it Inactive.
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
        return Result<SupplementProductResponse>.Success(_mapper.Map<SupplementProductResponse>(product));
    }

    public async Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(bool storeView, CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<string>>.Success(
            await _repository.GetCategoriesAsync(storeView ? StoreStatuses : AllStatuses, cancellationToken));

    public async Task<Result<SupplementProductResponse>> CreateAsync(
        SupplementProductRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = CleanName(request.Name);
        if (await _repository.ProductNameExistsAsync(name, null, cancellationToken))
            return NameExists();
        if (!EquipmentService.TryParse(request.Status, out SupplementProductStatus status))
            return InvalidStatus();

        var product = new SupplementProduct();
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

        var before = product.ProductStatus;
        product.ProductStatus = newStatus;
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={before}", $"Status={newStatus}");
        await AuditedWriter.SaveChangedAsync(_repository, product, audit, null, cancellationToken);
        return Result<SupplementProductResponse>.Success(_mapper.Map<SupplementProductResponse>(product));
    }

    // ── Helpers ──────────────────────────────────────────────────

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

        var (items, total) = await _repository.SearchProductsAsync(
            statuses, EquipmentService.Clean(request.Category), EquipmentService.Clean(request.Search),
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<SupplementProductResponse>>.Success(new PagedResponse<SupplementProductResponse>(
            request.Page, request.PageSize, total, items.Select(_mapper.Map<SupplementProductResponse>).ToArray()));
    }

    private static void Apply(SupplementProduct product, SupplementProductRequest request, string name, SupplementProductStatus status)
    {
        product.ProductName = name;
        product.Description = request.Description.Trim();
        product.CatalogCategory = EquipmentService.Clean(request.Category);
        product.ImageUrl = request.ImageUrl.Trim();
        product.UnitPrice = request.UnitPrice;
        product.ProductStatus = status;
    }

    private static string CleanName(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Describe(SupplementProduct p) =>
        $"Name={p.ProductName}; Category={p.CatalogCategory}; Price={p.UnitPrice}; Status={p.ProductStatus}";

    private static Result<SupplementProductResponse> NotFound() =>
        Result<SupplementProductResponse>.Failure("Supplement product was not found.", "SUPPLEMENT_PRODUCT_NOT_FOUND", 404);

    private static Result<SupplementProductResponse> NameExists() =>
        Result<SupplementProductResponse>.Failure(
            "A product with this name already exists.", "SUPPLEMENT_PRODUCT_NAME_EXISTS", 409);

    private static Result<SupplementProductResponse> InvalidStatus() =>
        Result<SupplementProductResponse>.Failure(
            "Status must be Active, Inactive or OutOfStock.", "SUPPLEMENT_PRODUCT_INVALID_STATUS");
}
