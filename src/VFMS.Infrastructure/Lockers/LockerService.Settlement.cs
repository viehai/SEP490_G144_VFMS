using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    private Task<LockerRentalContract?> StaffRental(ApplicationUser staff, int id, CancellationToken ct) =>
        Rentals.SingleOrDefaultAsync(r => r.Id == id && r.Locker.LockerZone.GymId == staff.GymId, ct);

    private static LockerSettlementQuote CalculateSettlement(LockerRentalContract rental, bool damaged, DateTime now)
    {
        var overdue = OverdueFees(rental, now);
        var key = damaged ? rental.KeyReplacementFeeSnapshot ?? rental.LockerPricingPolicy.KeyReplacementFee : 0;
        return SettleAmounts(rental, overdue, key);
    }

    private static LockerSettlementQuote SettleAmounts(LockerRentalContract rental, decimal overdue, decimal key)
    {
        var deposit = Type(rental) == RentalType.Daily ? rental.DepositAmount : 0;
        return new(overdue, key, deposit, Math.Max(0, deposit - overdue - key), Math.Max(0, overdue + key - deposit));
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
        if (!InUse(rental) || rental.Payments.Any(p => Pending(p, now) ||
            p.Purpose == LockerPaymentPurpose.Settlement && p.Order.OrderStatus == OrderStatus.Paid))
            return Conflict<LockerCheckout>("Hợp đồng không thể thu khoản tất toán mới.");
        if (ParseCollection(request.PaymentMethod) is not { } method)
            return Invalid<LockerCheckout>("Chọn Cash hoặc VietQR.");
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
        db.LockerRentalPayments.Add(charge);
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
            var paid = rental.Payments.SingleOrDefault(p => p.Purpose == LockerPaymentPurpose.Settlement &&
                p.Order.OrderStatus == OrderStatus.Paid);
            if (paid is not null)
            {
                if (paid.Order.OrderNumber != request.SettlementOrderNumber ||
                    paid.RentalExpiryAtQuote != RentalExpiry(rental) || paid.KeyReplacementFee != quote.KeyReplacementFee)
                    return Conflict<LockerRentalView>("Khoản đã thu không khớp biên bản trả tủ.");
                quote = SettleAmounts(rental, paid.OverdueFees, paid.KeyReplacementFee);
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
                var initial = rental.Payments.SingleOrDefault(p => p.Purpose == LockerPaymentPurpose.Rental);
                if (initial is null || initial.RefundedAt is not null)
                    return Conflict<LockerRentalView>("Không tìm thấy khoản cọc có thể hoàn.");
                initial.RefundAmount = quote.RefundAmount;
                initial.RefundReason = "DailyDepositReturn";
                RecordRefund(initial, actor, method, request.RefundReference, now);
            }
            rental.ContractStatus = LockerContractStatus.Terminated;
            rental.CancelledAt = now;
            rental.SettledAt = now;
            rental.SettledByUserId = actor;
            rental.FineAmount = quote.OverdueFees + quote.KeyReplacementFee;
            rental.Locker.LockerStatus = request.KeyLostOrDamaged || !request.LockSafe
                ? LockerStatus.Maintenance : LockerStatus.Available;
            Audit(actor, rental, FormattableString.Invariant(
                $"LockerTerminated:Overdue={quote.OverdueFees}:Replacement={quote.KeyReplacementFee}:Refund={quote.RefundAmount}"), now);
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
        var pending = await db.LockerRentalPayments.AsNoTracking().Include(p => p.Order)
            .ThenInclude(o => o.PaymentTransaction).ThenInclude(p => p!.Invoice)
            .Where(p => p.Rental.Locker.LockerZone.GymId == user.GymId && p.RefundAmount > 0 && p.RefundedAt == null)
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
        if (charge is null || charge.Rental.Locker.LockerZone.GymId != user.GymId)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        if (charge.RefundedAt is not null)
            return Result<LockerCheckout>.Success(Checkout(charge, now));
        if (charge.RefundAmount <= 0)
            return Conflict<LockerCheckout>("Đơn không có khoản cần hoàn.");
        if (ParseRefund(input.Method) is not { } method ||
            (method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(input.Reference)))
            return Invalid<LockerCheckout>("Cần ghi nhận cách hoàn tiền và mã tham chiếu nếu chuyển khoản.");
        RecordRefund(charge, actor, method, input.Reference, now);
        Audit(actor, charge.Rental, $"LockerRefundConfirmed:{charge.Order.OrderNumber}", now);
        Notify(charge.Rental, "Đã hoàn tiền thuê tủ", $"Khoản hoàn của đơn {orderNumber} đã được ghi nhận.", now);
        await db.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now));
    }, ct);

    private static void RecordRefund(LockerRentalPayment charge, string actor, PaymentMethod method, string? reference, DateTime now)
    {
        charge.RefundMethod = method;
        charge.RefundReference = reference?.Trim();
        charge.RefundedAt = now;
        charge.RefundedByUserId = actor;
        var payment = charge.Order.PaymentTransaction!;
        if (charge.RefundAmount == payment.AmountReceived)
            payment.PaymentStatus = PaymentStatus.Refunded;
    }
}
