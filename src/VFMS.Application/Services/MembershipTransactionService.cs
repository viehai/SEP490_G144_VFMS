using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>Read-only history of the purchaser's existing membership commerce records.</summary>
public sealed class MembershipTransactionService(IMembershipTransactionRepository repository) : IMembershipTransactionService
{
    public async Task<Result<PagedResponse<MembershipTransactionSummaryResponse>>> ListAsync(
        string purchaserId, int page, int pageSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(purchaserId))
            return Unauthorized<PagedResponse<MembershipTransactionSummaryResponse>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Result<PagedResponse<MembershipTransactionSummaryResponse>>.Failure(
                "Thông tin phân trang không hợp lệ.", "INVALID_PAGINATION");

        var (total, rows) = await repository.ListAsync(purchaserId, page, pageSize, ct);
        return Result<PagedResponse<MembershipTransactionSummaryResponse>>.Success(
            new(page, pageSize, total, rows.Select(ToSummary).ToArray()));
    }

    public async Task<Result<MembershipTransactionDetailResponse>> GetAsync(
        string purchaserId, int orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(purchaserId))
            return Unauthorized<MembershipTransactionDetailResponse>();

        var row = orderId > 0 ? await repository.FindAsync(purchaserId, orderId, ct) : null;
        if (row is null)
            return Result<MembershipTransactionDetailResponse>.Failure(
                "Không tìm thấy giao dịch.", "TRANSACTION_NOT_FOUND", 404);

        // Both operands are transaction snapshots. Never use today's catalog or voucher configuration.
        var subtotal = row.Items.Sum(item => item.UnitPrice);
        var discount = Math.Max(0, subtotal - row.Transaction.AmountDue);
        var voucher = row.Voucher is { } redemption &&
            (redemption.RedemptionStatus != VoucherRedemptionStatus.Released ||
                row.Transaction.OrderStatus is OrderStatus.Expired or OrderStatus.Cancelled && discount > 0)
            ? new MembershipTransactionVoucherResponse(redemption.Code, redemption.Name,
                redemption.DiscountAmount, redemption.RedemptionStatus.ToString()) : null;
        return Result<MembershipTransactionDetailResponse>.Success(new(ToSummary(row.Transaction), subtotal, discount,
            Utc(row.PaidAt), row.Items.Select(item => new MembershipTransactionItemResponse(item.PlanName,
                item.UnitPrice, item.IsForPurchaser, item.ActivationStartDate, item.IsRenewal)).ToArray(), voucher,
            row.Invoice is { } invoice
                ? new(invoice.InvoiceNumber, Utc(invoice.IssuedAt), invoice.InvoiceStatus.ToString()) : null,
            row.PurchaserName, row.Transaction.OrderStatus == OrderStatus.Paid &&
                row.Transaction.PaymentStatus == PaymentStatus.Paid && row.Invoice?.InvoiceStatus == InvoiceStatus.Issued));
    }

    private static MembershipTransactionSummaryResponse ToSummary(OwnedMembershipTransaction row) =>
        new(row.OrderId, row.OrderNumber, Utc(row.CreatedAt), Utc(row.CompletedAt), row.OrderStatus.ToString(),
            row.PaymentStatus.ToString(), row.PaymentMethod.ToString(), row.IsRenewal, row.PurchaseSummary,
            row.ItemCount, row.AmountDue, row.AmountReceived, row.InvoiceNumber);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? Utc(DateTime? value) => value is { } date ? Utc(date) : null;
    private static Result<T> Unauthorized<T>() => Result<T>.Failure("Phiên đăng nhập đã hết hạn.", "MSG-11", 401);
}
