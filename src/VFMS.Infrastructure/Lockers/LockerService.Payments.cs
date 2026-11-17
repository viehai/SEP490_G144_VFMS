using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Payments;

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
            await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.LockerId == lockerId ||
                r.ReplacementLockerId == lockerId || r.CustomerUserId == memberId, ct))
            return Conflict<LockerCheckout>("Tủ đã được giữ hoặc hội viên đã có tủ.");

        var policy = await db.LockerPricingPolicies.SingleOrDefaultAsync(p =>
            p.Id == policyId && p.PolicyStatus == PolicyStatus.Active, ct);
        if (policy is null || !ValidPolicy(policy) ||
            (channel == OrderChannel.Online && policy.RentalType == RentalType.Daily))
            return Invalid<LockerCheckout>("Gói thuê không hợp lệ cho kênh này.");
        if (policy.RentalType == RentalType.Daily && !OpenForDailyRental(locker.LockerZone.Gym, now))
            return Conflict<LockerCheckout>("Chỉ thu tiền và giao chìa thuê ngày trong giờ mở cửa.");

        var rental = new LockerRentalContract
        {
            Locker = locker,
            LockerPricingPolicy = policy,
            CustomerUserId = member.UserId,
            Customer = member,
            ContractNumber = $"LKR-{Guid.NewGuid():N}",
            StartDate = LocalDate(now),
            EndDate = LocalDate(now),
            ContractStatus = LockerContractStatus.PendingPayment,
            RentalTypeSnapshot = policy.RentalType,
            RentalPrice = policy.RentalPrice,
            DepositAmount = policy.DepositAmount,
            OverdueDailyFeeSnapshot = policy.OverdueDailyFee,
            KeyReplacementFeeSnapshot = policy.KeyReplacementFee
        };
        var charge = BuildCharge(rental, LockerPaymentPurpose.Rental, method, channel,
            rental.RentalPrice + rental.DepositAmount, now);
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        locker.LockerStatus = LockerStatus.Reserved;
        db.PaymentTransactions.Add(charge);
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
        var charge = await db.PaymentTransactions.AsNoTracking().Include(p => p.Order).Include(p => p.Invoice)
            .SingleOrDefaultAsync(p => p.Order.OrderNumber == number && p.LockerRentalContract!.Locker.LockerZone.GymId == user.GymId &&
                (role != UserRole.Member || p.LockerRentalContract!.CustomerUserId == actor), ct);
        if (charge is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        return Result<LockerCheckout>.Success(Checkout(charge, now,
            Pending(charge, now) && PaymentDeadline(charge) > now ? Qr(charge) : null));
    }

    public Task<Result<LockerCheckout>> CancelCheckout(string actor, UserRole role, string number, CancellationToken ct) =>
        Write(async () =>
        {
            var user = await Actor(actor, role, ct);
            if (user is null)
                return Forbidden<LockerCheckout>();
            var order = await commerce.FindOrderAsync(number, role == UserRole.Member ? actor : null, forUpdate: true, ct);
            var charge = order is null ? null : await LoadCharge(order.Id, ct);
            if (charge is null || charge.LockerRentalContract!.Locker.LockerZone.GymId != user.GymId)
                return Missing<LockerCheckout>();
            var now = Now.UtcDateTime;
            if (order!.OrderStatus == OrderStatus.Cancelled)
                return Result<LockerCheckout>.Success(Checkout(charge, now));
            if (!Pending(charge, now))
                return Conflict<LockerCheckout>("Chỉ có thể hủy đơn còn chờ thanh toán.");
            order.OrderStatus = OrderStatus.Cancelled;
            charge.PaymentStatus = PaymentStatus.Cancelled;
            await ReleaseInitialHold(charge, now, ct);
            Audit(actor, charge.LockerRentalContract!, $"LockerCheckoutCancelled:{number}", now);
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
            if (charge is null || charge.LockerRentalContract!.Locker.LockerZone.GymId != staff.GymId)
                return Missing<LockerCheckout>();
            var now = Now;
            if (order.OrderStatus == OrderStatus.Paid && payment.AmountReceived == amount)
                return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime));
            if (amount != payment.AmountDue || !Pending(charge, now.UtcDateTime) ||
                !await CanFulfill(charge, now.UtcDateTime, now.UtcDateTime, ct))
                return Conflict<LockerCheckout>("Kiểm tra số tiền, thời hạn và điều kiện của đơn Locker.");
            await ApplyPayment(charge, amount, now, null, now, actor, ct);
            return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime));
        }, ct);

    public async Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct)
    {
        var charge = await LoadCharge(order.Id, ct);
        if (charge is null || charge.SourcePaymentTransactionId is not null)
            return null;
        if (await CheckoutRefunds.IsRecorded(db, charge, transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Success(new(true));
        if (await commerce.SepayReferenceUsedAsync(transfer.TransactionId, ct) ||
            transfer.TransferredAt.ToUnixTimeSeconds() < new DateTimeOffset(Utc(order.CreatedAt)).ToUnixTimeSeconds())
            return Conflict<SepayWebhookResponse>("Không thể xác nhận khoản chuyển cho đơn Locker.");

        var now = Now;
        if (charge.GatewayTransactionReference is not null || order.OrderStatus == OrderStatus.Paid)
        {
            CheckoutRefunds.RecordExtraTransfer(db, charge, transfer, now.UtcDateTime);
            Audit(null, charge.LockerRentalContract!, $"LockerExtraTransfer:{order.OrderNumber}", now.UtcDateTime);
            await db.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        if (charge.AmountDue != transfer.Amount || transfer.TransferredAt.UtcDateTime >= PaymentDeadline(charge) ||
            !await CanFulfill(charge, transfer.TransferredAt.UtcDateTime, now.UtcDateTime, ct))
        {
            CheckoutRefunds.RecordUnappliedTransfer(db, charge, transfer, now.UtcDateTime);
            await ReleaseInitialHold(charge, now.UtcDateTime, ct);
            Audit(null, charge.LockerRentalContract!, "LockerRefundRequested", now.UtcDateTime);
            await db.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        await ApplyPayment(charge, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now, null, ct);
        return Result<SepayWebhookResponse>.Success(new(true));
    }

    private async Task<PaymentTransaction?> LoadCharge(int orderId, CancellationToken ct)
    {
        var id = await db.PaymentTransactions.Where(p => p.OrderId == orderId)
            .Select(p => (int?)p.LockerRentalContractId).SingleOrDefaultAsync(ct);
        if (id is null)
            return null;
        var rental = await Rentals.SingleAsync(r => r.Id == id, ct);
        return rental.Payments.Single(p => p.OrderId == orderId);
    }

    private static bool Pending(PaymentTransaction charge, DateTime now) =>
        charge.Order.OrderStatus == OrderStatus.Pending && charge.HoldExpiresAt > now;

    private static DateTime PaymentDeadline(PaymentTransaction charge) => charge.PaymentExpiresAt ?? HoldDeadline(charge);

    private static DateTime HoldDeadline(PaymentTransaction charge) => charge.HoldExpiresAt ?? charge.Order.CreatedAt;

    private async Task<bool> CanFulfill(PaymentTransaction charge, DateTime paidAt, DateTime now, CancellationToken ct)
    {
        var rental = charge.LockerRentalContract!;
        if ((charge.Order.OrderStatus, charge.PaymentStatus) is not
            ((OrderStatus.Pending, PaymentStatus.Pending) or (OrderStatus.Expired, PaymentStatus.Cancelled)))
            return false;
        if (charge.LockerPurpose == LockerPaymentPurpose.Settlement)
        {
            var quote = CalculateSettlement(rental, false, now);
            return InUse(rental) && RentalExpiry(rental) == charge.RentalExpiryAtQuote &&
                quote.KeyReplacementFee == charge.KeyReplacementFee &&
                (rental.ReturnedAt is null || quote.OverdueFees == charge.OverdueFees) &&
                !rental.Payments.Any(p => p.Id != charge.Id && IsPaidSettlement(p));
        }
        if (rental.Locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged ||
            !await Eligible(rental.CustomerUserId, now, ct))
            return false;
        if (charge.LockerPurpose == LockerPaymentPurpose.Renewal)
            return InUse(rental) && rental.ReturnedAt is null && paidAt < ClearanceAt(rental) &&
                RentalExpiry(rental) == charge.RentalExpiryAtQuote &&
                OverdueFees(rental, paidAt) == charge.OverdueFees &&
                !rental.Payments.Any(p => p.Id != charge.Id && p.LockerPurpose != LockerPaymentPurpose.Rental && Pending(p, now));
        if (Type(rental) == RentalType.Daily &&
            (LocalDate(paidAt) != LocalDate(now) || !OpenForDailyRental(rental.Locker.LockerZone.Gym, now)))
            return false;
        return (rental.ContractStatus == LockerContractStatus.PendingPayment ||
                rental.ContractStatus == LockerContractStatus.Cancelled && charge.Order.OrderStatus == OrderStatus.Expired) &&
            !await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.Id != rental.Id &&
                (r.CustomerUserId == rental.CustomerUserId || r.LockerId == rental.LockerId ||
                 r.ReplacementLockerId == rental.LockerId), ct);
    }

    private async Task ApplyPayment(PaymentTransaction charge, decimal amount, DateTimeOffset paidAt, string? reference,
        DateTimeOffset now, string? actor, CancellationToken ct)
    {
        var rental = charge.LockerRentalContract!;
        MembershipPurchaseService.RecordPaidOrder(charge.Order, amount, paidAt, reference, now);
        switch (charge.LockerPurpose)
        {
            case LockerPaymentPurpose.Rental:
                rental.CancelledAt = null;
                if (charge.Order.Channel == OrderChannel.Online)
                {
                    rental.CollectBy = Midnight(LocalDate(paidAt.UtcDateTime).AddDays(2));
                    SetMonthlyTerm(rental, LocalDate(rental.CollectBy.Value), Months(Type(rental)));
                }
                rental.ContractStatus = LockerContractStatus.PendingHandover;
                rental.Locker.LockerStatus = LockerStatus.Reserved;
                Notify(rental, "Đã thanh toán thuê tủ", $"Mang mã {rental.ContractNumber} đến lễ tân để nhận chìa.", now.UtcDateTime);
                break;
            case LockerPaymentPurpose.Renewal:
                var previousExpiry = RentalExpiry(rental);
                var start = paidAt.UtcDateTime < previousExpiry ? LocalDate(previousExpiry) : LocalDate(paidAt.UtcDateTime);
                if (paidAt.UtcDateTime >= previousExpiry)
                    rental.StartDate = start;
                rental.EndDate = start.AddMonths(charge.RenewalMonths!.Value).AddDays(-1);
                rental.ExpiresAt = Midnight(rental.EndDate.AddDays(1));
                rental.ContractStatus = LockerContractStatus.Active;
                rental.FinalNoticeAt = null;
                rental.FinalNoticeEmailSentAt = null;
                rental.FinalNoticeRetryAt = null;
                Notify(rental, "Đã gia hạn thuê tủ", "Thời hạn thuê tủ đã được cập nhật.", now.UtcDateTime);
                break;
            case LockerPaymentPurpose.Settlement:
                foreach (var other in rental.Payments.Where(p => p.Id != charge.Id &&
                    p.LockerPurpose == LockerPaymentPurpose.Settlement && p.Order.OrderStatus == OrderStatus.Pending))
                {
                    other.Order.OrderStatus = OrderStatus.Cancelled;
                    other.PaymentStatus = PaymentStatus.Cancelled;
                }
                break;
        }
        Audit(actor, rental, $"LockerPaymentConfirmed:{charge.LockerPurpose}:{charge.Order.OrderNumber}", now.UtcDateTime);
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

    private static PaymentTransaction BuildCharge(LockerRentalContract rental, LockerPaymentPurpose purpose,
        PaymentMethod method, OrderChannel channel, decimal amount, DateTime now)
    {
        var order = MembershipPurchaseService.NewOrder(rental.CustomerUserId, LocalDate(now), channel);
        var charge = new PaymentTransaction
        {
            UserId = order.PurchaserUserId,
            PaymentMethod = method,
            AmountDue = amount,
            Order = order,
            GatewayName = method == PaymentMethod.VietQR ? "SePay" : "Cash",
            LockerRentalContract = rental,
            LockerPurpose = purpose,
            PaymentExpiresAt = now.Add(PaymentWindow),
            HoldExpiresAt = now.Add(method == PaymentMethod.VietQR ? HoldWindow : PaymentWindow)
        };
        order.PaymentTransaction = charge;
        return charge;
    }

    private VietQrPaymentResponse? Qr(PaymentTransaction charge) =>
        charge.PaymentMethod == PaymentMethod.VietQR
            ? gateway.CreateVietQr(charge.Order.OrderNumber, charge.AmountDue) : null;

    private static LockerCheckout Checkout(PaymentTransaction charge, DateTime now, VietQrPaymentResponse? qr = null)
    {
        var order = charge.Order;
        var status = charge.RefundStatus == "Refunded" && charge.RefundAmount == charge.AmountReceived ? "Refunded" :
            charge.RefundStatus == "Failed" ? "RefundFailed" :
            charge.RefundStatus == "Pending" ? "RefundPending" :
            order.OrderStatus == OrderStatus.Pending && HoldDeadline(charge) <= now ? "Expired" :
            order.OrderStatus == OrderStatus.Pending && PaymentDeadline(charge) <= now ? "AwaitingConfirmation" :
            order.OrderStatus.ToString();
        return new(charge.LockerRentalContractId!.Value, order.OrderNumber, status, charge.AmountDue, Utc(PaymentDeadline(charge)),
            qr, charge.Invoice?.InvoiceNumber, charge.RefundAmount ?? 0, charge.RefundReason,
            charge.RefundStatus == "Refunded" && charge.RefundResolvedAt is { } refunded ? Utc(refunded) : null,
            Utc(HoldDeadline(charge)));
    }

    private static Result<LockerCheckout> PaymentUnavailable() =>
        Result<LockerCheckout>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
}
