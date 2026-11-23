namespace VFMS.Contracts.Memberships;

public sealed record StaffPackageOrderQuery
{
    public const string UnselectedMethod = "Unselected";
    public const string UnrecordedPayment = "Unrecorded";

    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? ServiceType { get; set; }
    public string? Method { get; set; }
    public string? PaymentStatus { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record StaffPackageOrderResponse(
    int OrderId, string OrderNumber, string PurchaserName, DateTimeOffset CreatedAt, DateTimeOffset PaymentDeadline,
    string Status, string? PaymentStatus, string? Method, decimal? AmountDue, decimal? AmountReceived,
    DateTimeOffset? PaidAt, string? InvoiceNumber, string? RefundStatus, bool IsPersonalTraining,
    bool CanConfirmCash, IReadOnlyList<string> ServiceTypes, IReadOnlyList<StaffPackageOrderItemResponse> Items);

public sealed record StaffPackageOrderItemResponse(string Name, decimal Amount);
