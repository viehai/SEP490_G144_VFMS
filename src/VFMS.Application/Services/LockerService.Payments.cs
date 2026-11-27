using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService
{
    public Task<Result<LockerCheckout>> Rent(string actor, LockerRentalRequest input, CancellationToken ct) =>
        StartRental(actor, actor, input.LockerId, input.PolicyId, PaymentMethod.VietQR, OrderChannel.Online, ct, input.ChooseAtCheckout);

    public Task<Result<LockerCheckout>> CounterRent(string actor, CounterLockerRentalRequest input, CancellationToken ct) =>
        ParseCollection(input.PaymentMethod) is { } method
            ? StartRental(actor, input.MemberId, input.LockerId, input.PolicyId, method, OrderChannel.Counter, ct, input.ChooseAtCheckout)
            : Task.FromResult(Invalid<LockerCheckout>("Chọn Cash hoặc VietQR."));

    private Task<Result<LockerCheckout>> StartRental(string actor, string memberId, int lockerId, int policyId,
        PaymentMethod method, OrderChannel channel, CancellationToken ct, bool chooseAtCheckout = false) => Write(async () =>
    {
        var user = await Actor(actor, channel == OrderChannel.Online ? UserRole.Member : UserRole.Staff, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        await commerce.LockUsersAsync([memberId], ct);
        var member = await Actor(memberId, UserRole.Member, ct);
        var now = Now.UtcDateTime;
        if (member is null || member.GymId != user.GymId)
            return Missing<LockerCheckout>();
        if (!await Eligible(memberId, now, ct))
            return MembershipRequired<LockerCheckout>();
        if (await repo.HasAllocationAsync(now, null, memberId, null, ct))
            return AlreadyHolding<LockerCheckout>();
        var unpaid = await repo.GetUnpaidAmountAsync(memberId, ct);
        if (unpaid > 0)
            return UnpaidAmount<LockerCheckout>(unpaid);
        var locker = await repo.GetGymLockerAsync(lockerId, user.GymId, ct);
        if (locker is null || locker.LockerStatus == LockerStatus.Inactive)
            return Missing<LockerCheckout>();
        if (locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged or LockerStatus.Rented ||
            await repo.HasAllocationAsync(now, lockerId, null, null, ct))
            return Conflict<LockerCheckout>("Tủ không còn trống.");

        var policy = await repo.GetActivePolicyAsync(policyId, ct);
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
            TermMonthsSnapshot = Months(policy),
            RentalPrice = policy.RentalType == RentalType.Daily ? 0 : policy.RentalPrice,
            DepositAmount = policy.DepositAmount,
            OverdueDailyFeeSnapshot = policy.OverdueDailyFee
        };
        var charge = BuildCharge(rental, LockerPaymentPurpose.Rental, method, channel,
            rental.RentalPrice + rental.DepositAmount, now);
        charge.PaymentStartedAt = channel == OrderChannel.Counter && !chooseAtCheckout ? now : null;
        if (chooseAtCheckout)
        {
            charge.HoldExpiresAt = now.Add(HoldWindow);
            charge.GatewayName = PendingGateway;
        }
        var qr = chooseAtCheckout ? null : Qr(charge);
        if (!chooseAtCheckout && method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        locker.LockerStatus = LockerStatus.Reserved;
        repo.Add(charge);
        await repo.SaveChangesAsync(ct);
        Audit(actor, rental, "LockerCheckoutHeld", now);
        await repo.SaveChangesAsync(ct);
        // GBR-109: an online rental shows the QR only after 'Proceed to Payment'.
        return Result<LockerCheckout>.Success(Checkout(charge, now, NeedsProceed(charge) ? null : qr), 201);
    }, ct);

    private static bool NeedsProceed(PaymentTransaction charge) =>
        charge.LockerPurpose == LockerPaymentPurpose.Rental && charge.Order.Channel == OrderChannel.Online &&
        charge.PaymentStartedAt is null;

    public Task<Result<LockerCheckout>> Proceed(string actor, string number, CancellationToken ct) =>
        Write(async () =>
        {
            var member = await Actor(actor, UserRole.Member, ct);
            if (member is null)
                return Forbidden<LockerCheckout>();
            var order = await commerce.FindOrderAsync(number, actor, forUpdate: true, ct);
            var charge = order is null ? null : await LoadCharge(order.Id, ct);
            if (charge is null || charge.LockerRentalContract!.Locker.LockerZone.GymId != member.GymId)
                return Missing<LockerCheckout>();
            var now = Now.UtcDateTime;
            if (charge.LockerPurpose != LockerPaymentPurpose.Rental || order!.Channel != OrderChannel.Online)
                return Conflict<LockerCheckout>("Chỉ đơn thuê tủ trực tuyến mới cần bắt đầu thanh toán.");
            if (charge.PaymentStartedAt is null)
            {
                if (!Pending(charge, now))
                    return Conflict<LockerCheckout>("Đơn không còn chờ thanh toán.");
                if (PaymentDeadline(charge) <= now)
                    return Conflict<LockerCheckout>("Đã quá hạn thanh toán. Đơn sẽ hết hạn, vui lòng thuê lại.");
                charge.PaymentStartedAt = now;
                await repo.SaveChangesAsync(ct);
            }
            var qr = Pending(charge, now) && PaymentDeadline(charge) > now ? Qr(charge) : null;
            return Result<LockerCheckout>.Success(Checkout(charge, now, qr));
        }, ct);

    public async Task<Result<LockerCheckout>> GetCheckout(string actor, UserRole role, string number, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        var charge = await repo.GetCheckoutAsync(number, user.GymId, role == UserRole.Member ? actor : null, ct);
        if (charge is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        return Result<LockerCheckout>.Success(Checkout(charge, now,
            Pending(charge, now) && PaymentDeadline(charge) > now && !ChoosingPayment(charge) ? Qr(charge) : null));
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
            ReleaseVoucher(charge);
            await ReleaseInitialHold(charge, now, ct);
            Audit(actor, charge.LockerRentalContract!, $"LockerCheckoutCancelled:{number}", now);
            await repo.SaveChangesAsync(ct);
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
        if (await repo.IsTransferRecordedAsync(charge, transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Success(new(true));
        if (await commerce.SepayReferenceUsedAsync(transfer.TransactionId, ct) ||
            transfer.TransferredAt.ToUnixTimeSeconds() < new DateTimeOffset(Utc(order.CreatedAt)).ToUnixTimeSeconds())
            return Conflict<SepayWebhookResponse>("Không thể xác nhận khoản chuyển cho đơn Locker.");

        var now = Now;
        if (charge.GatewayTransactionReference is not null || order.OrderStatus == OrderStatus.Paid)
        {
            repo.RecordExtraTransfer(charge, transfer, now.UtcDateTime);
            Audit(null, charge.LockerRentalContract!, $"LockerExtraTransfer:{order.OrderNumber}", now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        if (charge.AmountDue != transfer.Amount || transfer.TransferredAt.UtcDateTime >= PaymentDeadline(charge) ||
            !await CanFulfill(charge, transfer.TransferredAt.UtcDateTime, now.UtcDateTime, ct))
        {
            repo.RecordUnappliedTransfer(charge, transfer, now.UtcDateTime);
            ReleaseVoucher(charge);
            await ReleaseInitialHold(charge, now.UtcDateTime, ct);
            Audit(null, charge.LockerRentalContract!, "LockerRefundRequested", now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        await ApplyPayment(charge, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now, null, ct);
        return Result<SepayWebhookResponse>.Success(new(true));
    }

    private Task<PaymentTransaction?> LoadCharge(int orderId, CancellationToken ct) => repo.GetChargeByOrderAsync(orderId, ct);

    private static bool Pending(PaymentTransaction charge, DateTime now) =>
        charge.Order.OrderStatus == OrderStatus.Pending && charge.HoldExpiresAt > now;

    private static DateTime PaymentDeadline(PaymentTransaction charge) => charge.PaymentExpiresAt ?? HoldDeadline(charge);

    private static DateTime HoldDeadline(PaymentTransaction charge) => charge.HoldExpiresAt ?? charge.Order.CreatedAt;

    private async Task<bool> CanFulfill(PaymentTransaction charge, DateTime paidAt, DateTime now, CancellationToken ct, bool allowChoice = false)
    {
        var rental = charge.LockerRentalContract!;
        if (!allowChoice && ChoosingPayment(charge) || Discount(charge) > 0 && charge.Order.VoucherRedemption?.RedemptionStatus != VoucherRedemptionStatus.Reserved)
            return false;
        if ((charge.Order.OrderStatus, charge.PaymentStatus) is not
            ((OrderStatus.Pending, PaymentStatus.Pending) or (OrderStatus.Expired, PaymentStatus.Cancelled)))
            return false;
        if (charge.LockerPurpose == LockerPaymentPurpose.Settlement)
        {
            var quote = CalculateSettlement(rental, false, now);
            return InUse(rental) && RentalExpiry(rental) == charge.RentalExpiryAtQuote &&
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
            !await repo.HasAllocationAsync(now, rental.LockerId, rental.CustomerUserId, rental.Id, ct);
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
                // GBR-106: the key is collected by closing time on the second calendar day after payment.
                if (charge.Order.Channel == OrderChannel.Online)
                    rental.CollectBy = AtClose(LocalDate(paidAt.UtcDateTime).AddDays(2), rental.Locker.LockerZone.Gym);
                rental.ContractStatus = LockerContractStatus.PendingHandover;
                rental.Locker.LockerStatus = LockerStatus.Reserved;
                Notify(rental, "Đã thanh toán thuê tủ", rental.CollectBy is { } deadline
                    ? FormattableString.Invariant($"Mang mã {rental.ContractNumber} đến lễ tân nhận chìa trước {Local(deadline):HH:mm dd/MM/yyyy}.")
                    : $"Mang mã {rental.ContractNumber} đến lễ tân để nhận chìa.", now.UtcDateTime);
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
                rental.ReminderSentAt = null;
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
        await repo.SaveChangesAsync(ct);
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
            order.OrderStatus == OrderStatus.Pending && PaymentDeadline(charge) <= now && !ChoosingPayment(charge) ? "AwaitingConfirmation" :
            order.OrderStatus.ToString();
        return new(charge.LockerRentalContractId!.Value, order.OrderNumber, status, charge.AmountDue, Utc(PaymentDeadline(charge)),
            qr, charge.Invoice?.InvoiceNumber, charge.RefundAmount ?? 0, charge.RefundReason,
            charge.RefundStatus == "Refunded" && charge.RefundResolvedAt is { } refunded ? Utc(refunded) : null,
            Utc(HoldDeadline(charge)),
            NeedsProceed(charge) && order.OrderStatus == OrderStatus.Pending ? Utc(PaymentDeadline(charge)) : null,
            charge.PaymentMethod.ToString(), ChoosingPayment(charge) && Pending(charge, now) && PaymentDeadline(charge) > now,
            Discount(charge), order.VoucherRedemption?.VoucherCodeSnapshot);
    }

    private static Result<LockerCheckout> PaymentUnavailable() =>
        Result<LockerCheckout>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
}
