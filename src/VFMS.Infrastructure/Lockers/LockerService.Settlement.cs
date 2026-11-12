using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Payments;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    private Task<LockerRentalContract?> StaffRental(ApplicationUser staff, int id, CancellationToken ct) =>
        Rentals.SingleOrDefaultAsync(r => r.Id == id && r.Locker.LockerZone.GymId == staff.GymId, ct);

    private static bool IsPaidSettlement(PaymentTransaction payment) =>
        payment.LockerPurpose == LockerPaymentPurpose.Settlement && payment.Order.OrderStatus == OrderStatus.Paid;

    private static bool HasPendingOrPaidSettlement(LockerRentalContract rental, DateTime now) =>
        rental.Payments.Any(p => IsPaidSettlement(p) ||
            (p.LockerPurpose == LockerPaymentPurpose.Settlement && Pending(p, now)));

    private void MarkKeyLoss(string actor, LockerRentalContract rental, DateTime now)
    {
        rental.KeyLossConfirmedAt = now;
        Audit(actor, rental, "LockerKeyLossConfirmed", now);
    }

    private static LockerSettlementQuote CalculateSettlement(LockerRentalContract rental, bool damaged, DateTime now)
    {
        damaged |= rental.KeyLossConfirmedAt is not null;
        if (Type(rental) == RentalType.Daily)
        {
            return new(
                OverdueFees: 0,
                KeyReplacementFee: 0,
                DepositHeld: rental.DepositAmount,
                RefundAmount: damaged ? 0 : rental.DepositAmount,
                AdditionalAmountDue: 0,
                ForfeitedDeposit: damaged ? rental.DepositAmount : 0,
                ReturnedAt: rental.ReturnedAt is { } returned ? Utc(returned) : null);
        }

        var overdue = OverdueFees(rental, now);
        var key = damaged ? rental.KeyReplacementFeeSnapshot ?? rental.LockerPricingPolicy.KeyReplacementFee : 0;
        return SettleAmounts(rental, overdue, key);
    }

    public Task<Result<LockerSettlementQuote>> ConfirmKeyLoss(string actor, int id, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerSettlementQuote>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<LockerSettlementQuote>();
            if (!InUse(rental))
                return Conflict<LockerSettlementQuote>("Chỉ xác nhận chìa bị mất hoặc hỏng cần thay cho hợp đồng đang thuê hoặc quá hạn.");
            var now = Now.UtcDateTime;
            if (rental.KeyLossConfirmedAt is null)
            {
                if (HasPendingOrPaidSettlement(rental, now))
                    return Conflict<LockerSettlementQuote>("Cần xử lý khoản tất toán hiện có.");
                MarkKeyLoss(actor, rental, now);
                await db.SaveChangesAsync(ct);
            }
            return Result<LockerSettlementQuote>.Success(CalculateSettlement(rental, false, now));
        }, ct);

    private static LockerSettlementQuote SettleAmounts(LockerRentalContract rental, decimal overdue, decimal key)
    {
        var deposit = Type(rental) == RentalType.Daily ? rental.DepositAmount : 0;
        return new(overdue, key, deposit, Math.Max(0, deposit - overdue - key), Math.Max(0, overdue + key - deposit),
            ReturnedAt: rental.ReturnedAt is { } returned ? Utc(returned) : null);
    }

    public Task<Result<LockerSettlementQuote>> RecordReturn(string actor, int id, LockerReturnRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<LockerSettlementQuote>();
        var rental = await StaffRental(staff, id, ct);
        if (rental is null)
            return Missing<LockerSettlementQuote>();
        if (!InUse(rental))
            return Conflict<LockerSettlementQuote>("Hợp đồng chưa nhận chìa hoặc đã đóng.");
        if (!request.LockerEmpty)
            return Invalid<LockerSettlementQuote>("Cần xác nhận tủ đã trống.");

        var now = Now.UtcDateTime;
        if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
        {
            if (HasPendingOrPaidSettlement(rental, now))
                return Conflict<LockerSettlementQuote>("Cần xử lý khoản tất toán hiện có.");
            MarkKeyLoss(actor, rental, now);
        }
        MarkReturned(actor, rental, now);
        await db.SaveChangesAsync(ct);
        return Result<LockerSettlementQuote>.Success(CalculateSettlement(rental, false, now));
    }, ct);

    private void MarkReturned(string actor, LockerRentalContract rental, DateTime now)
    {
        if (rental.ReturnedAt is not null)
            return;
        rental.ReturnedAt = now;
        foreach (var renewal in rental.Payments.Where(p => p.LockerPurpose == LockerPaymentPurpose.Renewal &&
            p.Order.OrderStatus == OrderStatus.Pending))
        {
            renewal.Order.OrderStatus = OrderStatus.Cancelled;
            renewal.PaymentStatus = PaymentStatus.Cancelled;
        }
        ReleaseReplacement(rental);
        Audit(actor, rental, "LockerReturned", now);
    }

    public async Task<Result<LockerSettlementQuote>> SettlementQuote(
        string actor, int id, bool damaged, CancellationToken ct)
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<LockerSettlementQuote>();
        var rental = await StaffRental(staff, id, ct);
        if (rental is null)
            return Missing<LockerSettlementQuote>();
        return InUse(rental)
            ? Result<LockerSettlementQuote>.Success(CalculateSettlement(rental, damaged, Now.UtcDateTime))
            : Conflict<LockerSettlementQuote>("Hợp đồng chưa nhận chìa hoặc đã đóng.");
    }

    public Task<Result<LockerCheckout>> StartSettlement(string actor, int id, LockerSettlementRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<LockerCheckout>();
        var rental = await StaffRental(staff, id, ct);
        if (rental is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        var paid = rental.Payments
            .Where(IsPaidSettlement)
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (paid is not null)
            return Result<LockerCheckout>.Success(Checkout(paid, now));
        if (!InUse(rental) || rental.ReturnedAt is null)
            return Conflict<LockerCheckout>("Cần ghi nhận trả tủ trước khi thu phí.");
        if (ParseCollection(request.PaymentMethod) is not { } method)
            return Invalid<LockerCheckout>("Chọn Cash hoặc VietQR.");
        var pending = rental.Payments.FirstOrDefault(p =>
            p.LockerPurpose == LockerPaymentPurpose.Settlement && Pending(p, now));
        if (pending is not null)
        {
            if (pending.PaymentMethod != method)
                return Conflict<LockerCheckout>("Đã có giao dịch tất toán đang chờ.");
            var pendingQr = PaymentDeadline(pending) > now ? Qr(pending) : null;
            return Result<LockerCheckout>.Success(Checkout(pending, now, pendingQr));
        }
        if (rental.Payments.Any(p => Pending(p, now)))
            return Conflict<LockerCheckout>("Hợp đồng còn giao dịch đang chờ.");
        var quote = CalculateSettlement(rental, request.KeyLostOrDamaged, now);
        if (quote.AdditionalAmountDue <= 0)
            return Invalid<LockerCheckout>("Hợp đồng không có khoản thu thêm.");
        var charge = BuildCharge(rental, LockerPaymentPurpose.Settlement, method, OrderChannel.Counter,
            quote.AdditionalAmountDue, now);
        charge.OverdueFees = quote.OverdueFees;
        charge.KeyReplacementFee = quote.KeyReplacementFee;
        charge.RentalExpiryAtQuote = RentalExpiry(rental);
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
            MarkKeyLoss(actor, rental, now);

        db.PaymentTransactions.Add(charge);
        await db.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now, qr), 201);
    }, ct);

    public Task<Result<LockerRentalView>> Terminate(string actor, int id, LockerTerminationRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerRentalView>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var now = Now.UtcDateTime;
            if (rental.ContractStatus == LockerContractStatus.Terminated)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (!InUse(rental) || rental.Payments.Any(p => Pending(p, now)))
                return Conflict<LockerRentalView>("Hợp đồng đã đóng, chưa nhận chìa hoặc còn giao dịch đang chờ.");
            if (!request.LockerEmpty)
                return Invalid<LockerRentalView>("Cần xác nhận tủ đã trống.");

            var quote = CalculateSettlement(rental, request.KeyLostOrDamaged, now);
            var paidOrders = rental.Payments.Where(IsPaidSettlement).Take(2).ToArray();
            if (paidOrders.Length > 1)
                return Conflict<LockerRentalView>("Hợp đồng có nhiều khoản tất toán đã thu.");
            var paid = paidOrders.SingleOrDefault();
            if (paid is not null)
            {
                if (paid.Order.OrderNumber != request.SettlementOrderNumber ||
                    paid.RentalExpiryAtQuote != RentalExpiry(rental) || paid.KeyReplacementFee != quote.KeyReplacementFee)
                    return Conflict<LockerRentalView>("Khoản đã thu không khớp biên bản trả tủ.");
                quote = SettleAmounts(rental, paid.OverdueFees ?? 0, paid.KeyReplacementFee ?? 0);
            }
            else if (quote.AdditionalAmountDue > 0)
            {
                return Conflict<LockerRentalView>("Cần thu và xác nhận phí trước khi trả tủ.");
            }

            if (quote.RefundAmount > 0)
            {
                if (ParseRefund(request.RefundMethod) is not { } method ||
                    (method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.RefundReference)))
                    return Invalid<LockerRentalView>("Cần ghi nhận cách hoàn cọc và mã tham chiếu nếu chuyển khoản.");
                var initial = rental.Payments.SingleOrDefault(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
                if (initial is null || initial.RefundStatus is not null)
                    return Conflict<LockerRentalView>("Không tìm thấy khoản cọc có thể hoàn.");
                initial.RefundAmount = quote.RefundAmount;
                initial.RefundReason = "DailyDepositReturn";
                CheckoutRefunds.Resolve(db, initial, actor, true, method, request.RefundReference?.Trim(), now);
            }
            if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
                MarkKeyLoss(actor, rental, now);

            MarkReturned(actor, rental, now);
            rental.ContractStatus = LockerContractStatus.Terminated;
            rental.CancelledAt = now;
            rental.SettledAt = now;
            rental.SettledByUserId = actor;
            rental.FineAmount = quote.OverdueFees + quote.KeyReplacementFee + quote.ForfeitedDeposit;
            rental.Locker.LockerStatus = rental.KeyLossConfirmedAt is not null || !request.LockSafe
                ? LockerStatus.Maintenance : LockerStatus.Available;
            Audit(actor, rental, FormattableString.Invariant(
                $"LockerTerminated:Overdue={quote.OverdueFees}:Replacement={quote.KeyReplacementFee}:Refund={quote.RefundAmount}:Forfeited={quote.ForfeitedDeposit}"), now);
            Notify(rental, "Đã trả tủ", FormattableString.Invariant(
                $"Hợp đồng {rental.ContractNumber} đã kết thúc. Số tiền hoàn cọc: {quote.RefundAmount:N0} VND."), now);
            await db.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public async Task<Result<IReadOnlyList<LockerCheckout>>> Refunds(string actor, UserRole role, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<IReadOnlyList<LockerCheckout>>();
        var pending = await db.PaymentTransactions.AsNoTracking().Include(p => p.Order).Include(p => p.Invoice)
            .Where(p => p.LockerRentalContract!.Locker.LockerZone.GymId == user.GymId &&
                p.RefundAmount > 0 && p.RefundStatus != "Refunded")
            .OrderBy(p => p.Id).Take(100).ToListAsync(ct);
        var now = Now.UtcDateTime;
        return Result<IReadOnlyList<LockerCheckout>>.Success(pending.Select(p => Checkout(p, now)).ToArray());
    }

    public Task<Result<LockerCheckout>> ConfirmRefund(string actor, UserRole role, string orderNumber,
        LockerRefundRequest input, CancellationToken ct) => Write(async () =>
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        var order = await commerce.FindOrderAsync(orderNumber, null, forUpdate: true, ct);
        var charge = order is null ? null : await LoadCharge(order.Id, ct);
        if (charge is null || charge.LockerRentalContract!.Locker.LockerZone.GymId != user.GymId)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        if (charge.RefundStatus == "Refunded")
            return Result<LockerCheckout>.Success(Checkout(charge, now));
        if (charge.RefundAmount is not > 0)
            return Conflict<LockerCheckout>("Đơn không có khoản cần hoàn.");
        if (ParseRefund(input.Method) is not { } method ||
            (method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(input.Reference)))
            return Invalid<LockerCheckout>("Cần ghi nhận cách hoàn tiền và mã tham chiếu nếu chuyển khoản.");
        await db.Entry(charge).Reference(p => p.SourcePaymentTransaction).Query().Include(p => p.Order).LoadAsync(ct);
        CheckoutRefunds.Resolve(db, charge, actor, true, method, input.Reference?.Trim(), now);
        Audit(actor, charge.LockerRentalContract!, $"LockerRefundConfirmed:{charge.Order.OrderNumber}", now);
        await db.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now));
    }, ct);
}
