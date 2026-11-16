using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IMembershipTransactionService
{
    Task<Result<PagedResponse<MembershipTransactionSummaryResponse>>> ListAsync(
        string purchaserId, int page, int pageSize, CancellationToken ct);
    Task<Result<MembershipTransactionDetailResponse>> GetAsync(string purchaserId, int orderId, CancellationToken ct);
}

public interface IMembershipTransactionRepository
{
    Task<(int TotalCount, IReadOnlyList<OwnedMembershipTransaction> Items)> ListAsync(
        string purchaserId, int page, int pageSize, CancellationToken ct);
    Task<OwnedMembershipTransactionDetail?> FindAsync(string purchaserId, int orderId, CancellationToken ct);
}

public sealed record OwnedMembershipTransaction(
    int OrderId, string OrderNumber, DateTime CreatedAt, DateTime? CompletedAt,
    OrderStatus OrderStatus, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod, bool IsRenewal,
    string PurchaseSummary, int ItemCount, decimal AmountDue, decimal AmountReceived, string? InvoiceNumber);

public sealed record OwnedMembershipTransactionDetail(
    OwnedMembershipTransaction Transaction, DateTime? PaidAt, IReadOnlyList<OwnedMembershipTransactionItem> Items,
    OwnedMembershipTransactionVoucher? Voucher, OwnedMembershipTransactionInvoice? Invoice, string? PurchaserName = null);

public sealed record OwnedMembershipTransactionItem(
    string PlanName, decimal UnitPrice, bool IsForPurchaser, DateOnly? ActivationStartDate, bool IsRenewal);

public sealed record OwnedMembershipTransactionVoucher(
    string? Code, string? Name, decimal DiscountAmount, VoucherRedemptionStatus RedemptionStatus);

public sealed record OwnedMembershipTransactionInvoice(string InvoiceNumber, DateTime IssuedAt, InvoiceStatus InvoiceStatus);
