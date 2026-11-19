using System.ComponentModel.DataAnnotations;

namespace VFMS.Contracts.Common;

public sealed record CheckoutRefundView(int Id, string OrderNumber, decimal Amount, string Status,
    DateTime ReceivedAt, DateTime? ResolvedAt, string TransferReference, string? Reference);

public sealed record CheckoutRefundOutcome([Required] bool? Succeeded, [StringLength(100)] string? Reference);
