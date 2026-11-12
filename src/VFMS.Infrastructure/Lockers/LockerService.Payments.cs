using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    public Task<Result<LockerCheckout>> Rent(string actor, LockerRentalRequest input, CancellationToken ct) =>
        StartRental(actor, actor, input.LockerId, input.PolicyId, PaymentMethod.VietQR, OrderChannel.Online, ct);

    public Task<Result<LockerCheckout>> CounterRent(string actor, CounterLockerRentalRequest input, CancellationToken ct) =>
        ParseCollection(input.PaymentMethod) is { } method
            ? StartRental(actor, input.MemberId, input.LockerId, input.PolicyId, method, OrderChannel.Counter, ct)
            : Task.FromResult(Invalid<LockerCheckout>("Chọn Cash hoặc VietQR."));

    private Task<Result<LockerCheckout>> StartRental(string actor, string memberId, int lockerId, int policyId,
        PaymentMethod method, OrderChannel channel, CancellationToken ct) => Write(async () =>
    {
        var user = await Actor(actor, channel == OrderChannel.Online ? UserRole.Member : UserRole.Staff, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        await commerce.LockUsersAsync([memberId], ct);
        var member = await Actor(memberId, UserRole.Member, ct);
        var now = Now.UtcDateTime;
        if (member is null || member.GymId != user.GymId || !await Eligible(memberId, now, ct))
            return Conflict<LockerCheckout>("Hội viên cần có gói Gym đang hiệu lực.");
        var locker = await db.Lockers.Include(l => l.LockerZone).ThenInclude(z => z.Gym)
            .SingleOrDefaultAsync(l => l.Id == lockerId && l.LockerZone.GymId == user.GymId, ct);
        if (locker is null)
            return Missing<LockerCheckout>();
        if (locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged or LockerStatus.Rented ||
            await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.LockerId == lockerId || r.CustomerUserId == memberId, ct))
            return Conflict<LockerCheckout>("Tủ đã được giữ hoặc hội viên đã có tủ.");

        var policy = await db.LockerPricingPolicies.SingleOrDefaultAsync(p =>
            p.Id == policyId && p.PolicyStatus == PolicyStatus.Active, ct);
        if (policy is null || !ValidPolicy(policy) ||
            (channel == OrderChannel.Online && policy.RentalType == RentalType.Daily))
            return Invalid<LockerCheckout>("Gói thuê không hợp lệ cho kênh này.");

        var rental = new LockerRentalContract
        {
            Locker = locker, LockerPricingPolicy = policy, CustomerUserId = member.UserId, Customer = member,
            ContractNumber = $"LKR-{Guid.NewGuid():N}", StartDate = LocalDate(now), EndDate = LocalDate(now),
            ContractStatus = LockerContractStatus.PendingPayment, RentalTypeSnapshot = policy.RentalType,
            RentalPrice = policy.RentalPrice, DepositAmount = policy.DepositAmount,
            OverdueDailyFeeSnapshot = policy.OverdueDailyFee, KeyReplacementFeeSnapshot = policy.KeyReplacementFee
        };
        var charge = BuildCharge(rental, LockerPaymentPurpose.Rental, method, channel,
            rental.RentalPrice + rental.DepositAmount, now);
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        locker.LockerStatus = LockerStatus.Reserved;
        db.LockerRentalPayments.Add(charge);
        await db.SaveChangesAsync(ct);
        Audit(actor, rental, "LockerCheckoutHeld", now);
        await db.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now, qr), 201);
    }, ct);

    public async Task<Result<LockerCheckout>> GetCheckout(string actor, UserRole role, string number, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        var charge = await db.LockerRentalPayments.AsNoTracking().Include(p => p.Order)
            .ThenInclude(o => o.PaymentTransaction).ThenInclude(p => p!.Invoice)
            .SingleOrDefaultAsync(p => p.Order.OrderNumber == number && p.Rental.Locker.LockerZone.GymId == user.GymId &&
                (role != UserRole.Member || p.Rental.CustomerUserId == actor), ct);
        if (charge is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        return Result<LockerCheckout>.Success(Checkout(charge, now, Pending(charge, now) ? Qr(charge) : null));
    }

    public Task<Result<LockerCheckout>> CancelCheckout(string actor, UserRole role, string number, CancellationToken ct) =>
        Write(async () =>
        {
            var user = await Actor(actor, role, ct);
            if (user is null)
                return Forbidden<LockerCheckout>();
            var order = await commerce.FindOrderAsync(number, role == UserRole.Member ? actor : null, forUpdate: true, ct);
            var charge = order is null ? null : await LoadCharge(order.Id, ct);
            if (charge is null || charge.Rental.Locker.LockerZone.GymId != user.GymId)
                return Missing<LockerCheckout>();
            var now = Now.UtcDateTime;
            if (order!.OrderStatus == OrderStatus.Cancelled)
                return Result<LockerCheckout>.Success(Checkout(charge, now));
            if (!Pending(charge, now))
                return Conflict<LockerCheckout>("Chỉ có thể hủy đơn còn chờ thanh toán.");
            order.OrderStatus = OrderStatus.Cancelled;
            order.PaymentTransaction!.PaymentStatus = PaymentStatus.Cancelled;
            await ReleaseInitialHold(charge, now, ct);
            Audit(actor, charge.Rental, $"LockerCheckoutCancelled:{number}", now);
            await db.SaveChangesAsync(ct);
            return Result<LockerCheckout>.Success(Checkout(charge, now));
        }, ct);

    public Task<Result<LockerCheckout>> ConfirmCash(string actor, string number, decimal amount, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerCheckout>();
            var order = await commerce.FindOrderAsync(number, null, forUpdate: true, ct);
            if (order?.PaymentTransaction is not { PaymentMethod: PaymentMethod.Cash } payment)
                return Missing<LockerCheckout>();
            var charge = await LoadCharge(order.Id, ct);
            if (charge is null || charge.Rental.Locker.LockerZone.GymId != staff.GymId)
                return Missing<LockerCheckout>();
            var now = Now;
            if (order.OrderStatus == OrderStatus.Paid && payment.AmountReceived == amount)
                return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime));
            if (amount != payment.AmountDue || !await CanFulfill(charge, now.UtcDateTime, ct))
                return Conflict<LockerCheckout>("Kiểm tra số tiền, thời hạn và điều kiện của đơn Locker.");
            await ApplyPayment(charge, amount, now, null, now, actor, ct);
            return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime));
        }, ct);

    public async Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct)
    {
        var charge = await LoadCharge(order.Id, ct);
        if (charge is null)
            return null;
        var payment = order.PaymentTransaction!;
        if (payment.GatewayTransactionReference == transfer.TransactionId ||
            await db.LockerRentalPayments.AnyAsync(p => p.SourceOrderId == order.Id &&
                p.Order.PaymentTransaction!.GatewayTransactionReference == transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Success(new(true));
        if (await commerce.SepayReferenceUsedAsync(transfer.TransactionId, ct) ||
            transfer.TransferredAt.ToUnixTimeSeconds() < new DateTimeOffset(Utc(order.CreatedAt)).ToUnixTimeSeconds())
            return Conflict<SepayWebhookResponse>("Không thể xác nhận khoản chuyển cho đơn Locker.");

        var now = Now;
        if (payment.GatewayTransactionReference is not null || order.OrderStatus == OrderStatus.Paid)
        {
            var extra = BuildCharge(charge.Rental, LockerPaymentPurpose.UnappliedTransfer, PaymentMethod.VietQR,
                order.Channel, transfer.Amount, now.UtcDateTime);
            extra.SourceOrderId = order.Id;
            RecordUnappliedTransfer(extra, transfer);
            db.LockerRentalPayments.Add(extra);
            Audit(null, charge.Rental, $"LockerExtraTransfer:{order.OrderNumber}", now.UtcDateTime);
            await db.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        if (payment.AmountDue != transfer.Amount || transfer.TransferredAt.UtcDateTime >= charge.HoldExpiresAt ||
            !await CanFulfill(charge, now.UtcDateTime, ct))
        {
            RecordUnappliedTransfer(charge, transfer);
            await ReleaseInitialHold(charge, now.UtcDateTime, ct);
            Audit(null, charge.Rental, Reconciliation, now.UtcDateTime);
            await db.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        await ApplyPayment(charge, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now, null, ct);
        return Result<SepayWebhookResponse>.Success(new(true));
    }

    private static void RecordUnappliedTransfer(LockerRentalPayment charge, SepayTransfer transfer)
    {
        var payment = charge.Order.PaymentTransaction!;
        payment.GatewayTransactionReference = transfer.TransactionId;
        payment.AmountReceived = transfer.Amount;
        payment.PaidAt = transfer.TransferredAt.UtcDateTime;
        payment.RawGatewayStatus = Reconciliation;
        payment.PaymentStatus = PaymentStatus.Cancelled;
        charge.Order.OrderStatus = OrderStatus.Expired;
        charge.RefundAmount = transfer.Amount;
        charge.RefundReason = Reconciliation;
    }

    private async Task<LockerRentalPayment?> LoadCharge(int orderId, CancellationToken ct)
    {
        var id = await db.LockerRentalPayments.Where(p => p.OrderId == orderId)
            .Select(p => (int?)p.LockerRentalContractId).SingleOrDefaultAsync(ct);
        if (id is null)
            return null;
        var rental = await Rentals.SingleAsync(r => r.Id == id, ct);
        return rental.Payments.Single(p => p.OrderId == orderId);
    }

    private static bool Pending(LockerRentalPayment charge, DateTime now) =>
        charge.Order.OrderStatus == OrderStatus.Pending && charge.HoldExpiresAt > now;

    private async Task<bool> CanFulfill(LockerRentalPayment charge, DateTime now, CancellationToken ct)
    {
        var rental = charge.Rental;
        if (!Pending(charge, now) || charge.Order.PaymentTransaction?.PaymentStatus != PaymentStatus.Pending)
            return false;
        if (charge.Purpose == LockerPaymentPurpose.Settlement)
            return InUse(rental) && RentalExpiry(rental) == charge.RentalExpiryAtQuote;
        if (rental.Locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged ||
            !await Eligible(rental.CustomerUserId, now, ct))
            return false;
        if (charge.Purpose == LockerPaymentPurpose.Renewal)
            return InUse(rental) && OverdueDays(rental, now) <= 7 && RentalExpiry(rental) == charge.RentalExpiryAtQuote &&
                OverdueFees(rental, now) == charge.OverdueFees;
        return rental.ContractStatus == LockerContractStatus.PendingPayment &&
            !await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.Id != rental.Id &&
                (r.CustomerUserId == rental.CustomerUserId || r.LockerId == rental.LockerId), ct);
    }

    private async Task ApplyPayment(LockerRentalPayment charge, decimal amount, DateTimeOffset paidAt, string? reference,
        DateTimeOffset now, string? actor, CancellationToken ct)
    {
        var rental = charge.Rental;
        MembershipPurchaseService.RecordPaidOrder(charge.Order, amount, paidAt, reference, now);
        switch (charge.Purpose)
        {
            case LockerPaymentPurpose.Rental:
                rental.CollectBy = paidAt.UtcDateTime.AddHours(24);
                rental.ContractStatus = LockerContractStatus.PendingHandover;
                rental.Locker.LockerStatus = LockerStatus.Reserved;
                Notify(rental, "Đã thanh toán thuê tủ", $"Mang mã {rental.ContractNumber} đến lễ tân để nhận chìa.", now.UtcDateTime);
                break;
            case LockerPaymentPurpose.Renewal:
                var previousExpiry = RentalExpiry(rental);
                var start = previousExpiry > now.UtcDateTime ? previousExpiry : now.UtcDateTime;
                rental.ExpiresAt = start.AddMonths(charge.RenewalMonths!.Value);
                rental.EndDate = LocalDate(rental.ExpiresAt.Value);
                rental.ContractStatus = LockerContractStatus.Active;
                rental.FinalNoticeAt = null;
                rental.FinalNoticeEmailSentAt = null;
                rental.FinalNoticeRetryAt = null;
                Notify(rental, "Đã gia hạn thuê tủ", "Thời hạn thuê tủ đã được cập nhật.", now.UtcDateTime);
                break;
        }
        Audit(actor, rental, $"LockerPaymentConfirmed:{charge.Purpose}:{charge.Order.OrderNumber}", now.UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    private static PaymentMethod? ParseCollection(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "CASH" => PaymentMethod.Cash,
        "VIETQR" => PaymentMethod.VietQR,
        _ => null
    };

    private static PaymentMethod? ParseRefund(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "CASH" => PaymentMethod.Cash,
        "BANKTRANSFER" => PaymentMethod.BankTransfer,
        _ => null
    };

    private static LockerRentalPayment BuildCharge(LockerRentalContract rental, LockerPaymentPurpose purpose,
        PaymentMethod method, OrderChannel channel, decimal amount, DateTime now)
    {
        var order = MembershipPurchaseService.NewOrder(rental.CustomerUserId, LocalDate(now), channel);
        order.PaymentTransaction = new PaymentTransaction
        {
            UserId = order.PurchaserUserId, PaymentMethod = method, AmountDue = amount,
            GatewayName = method == PaymentMethod.VietQR ? "SePay" : "Cash"
        };
        return new LockerRentalPayment { Rental = rental, Order = order, Purpose = purpose, HoldExpiresAt = now.Add(PaymentWindow) };
    }

    private VietQrPaymentResponse? Qr(LockerRentalPayment charge) =>
        charge.Order.PaymentTransaction is { PaymentMethod: PaymentMethod.VietQR } payment
            ? gateway.CreateVietQr(charge.Order.OrderNumber, payment.AmountDue) : null;

    private static LockerCheckout Checkout(LockerRentalPayment charge, DateTime now, VietQrPaymentResponse? qr = null)
    {
        var order = charge.Order;
        var payment = order.PaymentTransaction!;
        var status = charge.RefundedAt is not null && charge.RefundAmount == payment.AmountReceived ? "Refunded" :
            payment.RawGatewayStatus == Reconciliation ? "ReconciliationRequired" :
            order.OrderStatus == OrderStatus.Pending && charge.HoldExpiresAt <= now ? "Expired" : order.OrderStatus.ToString();
        return new(charge.LockerRentalContractId, order.OrderNumber, status, payment.AmountDue, Utc(charge.HoldExpiresAt),
            qr, payment.Invoice?.InvoiceNumber, charge.RefundAmount, charge.RefundReason,
            charge.RefundedAt is { } refunded ? Utc(refunded) : null);
    }

    private static Result<LockerCheckout> PaymentUnavailable() =>
        Result<LockerCheckout>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
}
