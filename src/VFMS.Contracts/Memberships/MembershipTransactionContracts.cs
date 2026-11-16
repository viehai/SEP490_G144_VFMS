namespace VFMS.Contracts.Memberships;

public sealed record MembershipTransactionSummaryResponse(
    int OrderId, string OrderNumber, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    string OrderStatus, string PaymentStatus, string PaymentMethod, bool IsRenewal,
    string PurchaseSummary, int ItemCount, decimal AmountDue, decimal AmountReceived, string? InvoiceNumber);

public sealed record MembershipTransactionDetailResponse(
    MembershipTransactionSummaryResponse Transaction, decimal Subtotal, decimal DiscountAmount,
    DateTimeOffset? PaidAt, IReadOnlyList<MembershipTransactionItemResponse> Items,
    MembershipTransactionVoucherResponse? Voucher, MembershipTransactionInvoiceResponse? Invoice,
    string? PurchaserName = null, bool CanExportInvoicePdf = false);

public sealed record MembershipTransactionItemResponse(
    string PlanName, decimal UnitPrice, bool IsForPurchaser, DateOnly? ActivationStartDate, bool IsRenewal);

public sealed record MembershipTransactionVoucherResponse(
    string? Code, string? Name, decimal DiscountAmount, string RedemptionStatus);

public sealed record MembershipTransactionInvoiceResponse(
    string InvoiceNumber, DateTimeOffset IssuedAt, string InvoiceStatus);
