using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Payments;

public static class CheckoutRefunds
{
    public static async Task<bool> IsRecorded(VfmsDbContext db, PaymentTransaction original, string reference,
        CancellationToken ct) =>
        original.GatewayTransactionReference == reference ||
        await db.PaymentTransactions.AnyAsync(p => p.SourcePaymentTransactionId == original.Id &&
            p.GatewayName == original.GatewayName && p.GatewayTransactionReference == reference, ct);

    public static void RecordExtraTransfer(VfmsDbContext db, PaymentTransaction original, SepayTransfer transfer, DateTime now)
    {
        var date = DateOnly.FromDateTime(now.AddHours(7));
        var order = MembershipPurchaseService.NewOrder(original.Order.PurchaserUserId, date, original.Order.Channel);
        var extra = new PaymentTransaction
        {
            Order = order,
            GatewayName = original.GatewayName,
            PaymentMethod = original.PaymentMethod,
            AmountDue = transfer.Amount,
            SourcePaymentTransaction = original,
            LockerRentalContractId = original.LockerRentalContractId,
            LockerPurpose = original.LockerRentalContractId is null ? null : LockerPaymentPurpose.UnappliedTransfer,
            PaymentExpiresAt = original.PaymentExpiresAt,
            HoldExpiresAt = original.HoldExpiresAt
        };
        order.PaymentTransaction = extra;
        db.PaymentTransactions.Add(extra);
        RecordUnappliedTransfer(db, extra, transfer, now);
    }

    // notify: false when the caller sends its own in-app notice for the same transfer (PT checkout).
    public static void RecordUnappliedTransfer(VfmsDbContext db, PaymentTransaction payment, SepayTransfer transfer, DateTime now,
        bool notify = true)
    {
        payment.GatewayTransactionReference = transfer.TransactionId;
        payment.AmountReceived = transfer.Amount;
        payment.PaidAt = transfer.TransferredAt.UtcDateTime;
        payment.RawGatewayStatus = "CHECKOUT_REFUND_REQUESTED";
        payment.PaymentStatus = PaymentStatus.Cancelled;
        payment.Order.OrderStatus = OrderStatus.Expired;
        Request(db, payment, "CheckoutUnfulfilled", now, notify);
    }

    public static void Request(VfmsDbContext db, PaymentTransaction payment, string reason, DateTime now, bool notify = true)
    {
        payment.RefundAmount = payment.AmountReceived;
        payment.RefundReason = reason;
        payment.RefundStatus = "Pending";
        if (!notify)
            return;
        var order = payment.SourcePaymentTransaction?.Order ?? payment.Order;
        Notify(db, order, "Thanh toán chưa được áp dụng",
            $"Đã nhận {payment.RefundAmount:N0} VND cho đơn {order.OrderNumber} nhưng chưa thể xác nhận đơn. " +
            "Vui lòng liên hệ quầy lễ tân để được xử lý.", now);
    }

    public static void Resolve(VfmsDbContext db, PaymentTransaction payment, string actor, bool succeeded,
        PaymentMethod? method, string? reference, DateTime now)
    {
        payment.RefundStatus = succeeded ? "Refunded" : "Failed";
        payment.RefundedByUserId = actor;
        payment.RefundResolvedAt = now;
        payment.RefundReference = reference;
        if (succeeded)
        {
            payment.RefundMethod = method;
            if (payment.RefundAmount == payment.AmountReceived)
                payment.PaymentStatus = PaymentStatus.Refunded;
        }
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = nameof(PaymentTransaction),
            EntityId = payment.Id,
            NewValue = payment.RefundStatus,
            OccurredAt = now
        });
        var order = payment.SourcePaymentTransaction?.Order ?? payment.Order;
        Notify(db, order, "Hoàn tiền", succeeded
            ? $"Đã hoàn {payment.RefundAmount:N0} VND cho đơn {order.OrderNumber}."
            : $"Chưa thể hoàn tiền cho đơn {order.OrderNumber}.", now);
    }

    private static void Notify(VfmsDbContext db, Order order, string title, string message, DateTime now) =>
        db.Notifications.Add(new Notification
        {
            RecipientUserId = order.PurchaserUserId,
            NotificationType = "Payment",
            Title = title,
            Message = message,
            NotifiedAt = now,
            EntityType = nameof(Order),
            EntityId = order.Id
        });
}
