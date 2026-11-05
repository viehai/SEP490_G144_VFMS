using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface ISupplementRepository : IAuditedRepository
{
    // Products
    Task<(IReadOnlyList<SupplementProduct> Items, int TotalCount)> SearchProductsAsync(
        IReadOnlyCollection<SupplementProductStatus> statuses, string? category, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SupplementProduct?> GetProductAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ProductNameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(
        IReadOnlyCollection<SupplementProductStatus> statuses, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplementProduct>> GetProductsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    // Orders
    Task<(IReadOnlyList<SupplementOrder> Items, int TotalCount)> SearchOrdersAsync(
        string? customerUserId, IReadOnlyCollection<SupplementOrderStatus>? statuses, SupplementDeliveryType? deliveryType,
        DateTime? fromUtc, DateTime? toUtcExclusive, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SupplementOrder?> GetOrderAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default);
}

/// <summary>UC-77 View Supplement Products and UC-83 Manage Supplement Products. GBR-69, GBR-70.</summary>
public interface ISupplementProductService
{
    /// <summary>Store view: Active and OutOfStock products only.</summary>
    Task<Result<PagedResponse<SupplementProductResponse>>> GetStoreProductsAsync(
        SupplementProductListRequest request, CancellationToken cancellationToken = default);
    /// <summary>Manager view: every status.</summary>
    Task<Result<PagedResponse<SupplementProductResponse>>> GetAllProductsAsync(
        SupplementProductListRequest request, CancellationToken cancellationToken = default);
    /// <summary>When <paramref name="storeView"/> is true, Inactive products are not found.</summary>
    Task<Result<SupplementProductResponse>> GetProductAsync(int id, bool storeView, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(bool storeView, CancellationToken cancellationToken = default);

    Task<Result<SupplementProductResponse>> CreateAsync(
        SupplementProductRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<SupplementProductResponse>> UpdateAsync(
        int id, SupplementProductRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<SupplementProductResponse>> ChangeStatusAsync(
        int id, string status, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>
/// UC-78 Purchase, UC-79 Cancel, UC-80 Track, UC-81 History (Customer, Member) and UC-82 Manage Supplement Orders (Staff, Manager).
/// GBR-57..61.
/// </summary>
public interface ISupplementOrderService
{
    // Customer / Member
    Task<Result<SupplementOrderResponse>> PlaceOrderAsync(
        PlaceSupplementOrderRequest request, string customerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<SupplementOrderResponse>>> GetHistoryAsync(
        SupplementOrderListRequest request, string customerUserId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<SupplementOrderResponse>>> GetOngoingAsync(
        string customerUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<SupplementOrderResponse>> CancelAsync(
        int id, string customerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>When <paramref name="onlyCustomerUserId"/> is set, other customers' orders are not found.</summary>
    Task<Result<SupplementOrderResponse>> GetAsync(int id, string? onlyCustomerUserId, CancellationToken cancellationToken = default);

    // Staff / Manager
    Task<Result<PagedResponse<SupplementOrderResponse>>> GetAllAsync(
        SupplementOrderListRequest request, CancellationToken cancellationToken = default);
    Task<Result<SupplementOrderResponse>> ProcessAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<SupplementOrderResponse>> ReadyForPickupAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<SupplementOrderResponse>> OutForDeliveryAsync(
        int id, SupplementOutForDeliveryRequest request, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    /// <summary>Confirms the payment (counter or COD) and completes the order (GBR-59).</summary>
    Task<Result<SupplementOrderResponse>> CompleteAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
