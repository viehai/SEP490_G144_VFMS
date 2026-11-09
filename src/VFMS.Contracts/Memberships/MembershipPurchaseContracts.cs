namespace VFMS.Contracts.Memberships;

public static class MembershipPaymentMethods
{
    public const string VietQr = "VietQR";
    public const string Cash = "Cash";
}

public static class MembershipPurchaseStatuses
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
}

public static class MembershipPurchaseMessages
{
    public const string PaidCode = "MSG-23";
    public const string Paid = "Thanh toán thành công.";
    public const string FailedCode = "MSG-24";
    public const string Failed = "Giao dịch trực tuyến thất bại hoặc đã hết hiệu lực.";
    public const string ActivationDateCode = "MSG-25";
    public const string ActivationDate = "Chọn ngày kích hoạt từ hôm nay đến tối đa 30 ngày tới.";
    public const string RenewalBlockedCode = "MSG-26";
    public const string RenewalBlocked = "Không thể gia hạn gói hội viên đang đóng băng hoặc bị tạm đình chỉ.";
}

public sealed record RenewMembershipRequest(int MembershipId);

public sealed record MembershipRenewalOption(
    int MembershipId, string PlanName, string Status, DateOnly EndDate, DateOnly? RenewalStartDate,
    decimal? Price, bool Eligible, string? MessageCode, string? Message);

public sealed record MembershipPurchaseRequest(
    IReadOnlyList<MembershipPurchaseItemRequest> Items, string PaymentMethod);

public sealed record MembershipPurchaseItemRequest(
    int PlanId, string? BeneficiaryEmail = null, DateOnly? ActivationDate = null);

public sealed record MembershipPurchaseItemResponse(
    int PlanId, string PlanName, bool IsForPurchaser, DateOnly? ActivationDate, decimal UnitPrice);

public sealed record VietQrPaymentResponse(
    string ImageUrl, string BankId, string AccountNumber, string AccountName, string TransferContent);

public sealed record MembershipPurchaseResponse(
    string OrderNumber, string OrderStatus, string PaymentStatus, string PaymentMethod,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, decimal Subtotal, decimal AmountDue,
    IReadOnlyList<MembershipPurchaseItemResponse> Items, VietQrPaymentResponse? VietQr,
    string? InvoiceNumber, string? MessageCode, string? Message);

public sealed record ConfirmMembershipCashRequest(decimal AmountReceived);

public sealed record SepayWebhookRequest(
    long Id, string? TransactionDate, string? AccountNumber, string? Code,
    string? Content, string? TransferType, decimal TransferAmount);

public sealed record SepayWebhookResponse(bool Success);
