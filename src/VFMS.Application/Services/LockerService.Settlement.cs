using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService
{
    private Task<LockerRentalContract?> StaffRental(ApplicationUser staff, int id, CancellationToken ct) =>
        repo.GetRentalAsync(id, staff.GymId, null, ct);

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

    // GBR-20: a monthly locker overdue by more than 7 days is cleared by Staff and Manager, never returned normally.
    private static bool PastGrace(LockerRentalContract rental, DateTime now) =>
        Type(rental) != RentalType.Daily && rental.ReturnedAt is null && OverdueDays(rental, now) > 7;

    private static Result<T> MustClear<T>() => Conflict<T>(
        "Hợp đồng quá hạn hơn 7 ngày, cần xử lý theo quy trình dọn tủ có quản lý xác nhận.");

    private static LockerSettlementQuote CalculateSettlement(LockerRentalContract rental, bool damaged, DateTime now) =>
        SettleAmounts(rental, OverdueFees(rental, now), damaged || rental.KeyLossConfirmedAt is not null, now);

    // GBR-19: the deposit is returned in full unless the key is lost or damaged, or a Daily key comes back after closing time.
    // GBR-20: the overdue fee is collected in cash before the contract closes, so it never reduces the deposit.
    private static LockerSettlementQuote SettleAmounts(LockerRentalContract rental, decimal overdue, bool damaged, DateTime now)
    {
        var lateDaily = Type(rental) == RentalType.Daily && (rental.ReturnedAt ?? now) > RentalExpiry(rental);
        var forfeited = damaged || lateDaily;
        return new(overdue, rental.DepositAmount, forfeited ? 0 : rental.DepositAmount, overdue,
            forfeited ? rental.DepositAmount : 0, rental.ReturnedAt is { } returned ? Utc(returned) : null);
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
            if (PastGrace(rental, now))
                return MustClear<LockerSettlementQuote>();
            if (rental.KeyLossConfirmedAt is null)
            {
                if (HasPendingOrPaidSettlement(rental, now))
                    return Conflict<LockerSettlementQuote>("Cần xử lý khoản tất toán hiện có.");
                MarkKeyLoss(actor, rental, now);
                await repo.SaveChangesAsync(ct);
            }
            return Result<LockerSettlementQuote>.Success(CalculateSettlement(rental, false, now));
        }, ct);

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
        if (PastGrace(rental, now))
            return MustClear<LockerSettlementQuote>();
        if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
        {
            if (HasPendingOrPaidSettlement(rental, now))
                return Conflict<LockerSettlementQuote>("Cần xử lý khoản tất toán hiện có.");
            MarkKeyLoss(actor, rental, now);
        }
        MarkReturned(actor, rental, now);
        await repo.SaveChangesAsync(ct);
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
        charge.RentalExpiryAtQuote = RentalExpiry(rental);
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
            MarkKeyLoss(actor, rental, now);

        repo.Add(charge);
        await repo.SaveChangesAsync(ct);
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
            if (PastGrace(rental, now))
                return MustClear<LockerRentalView>();

            var damaged = request.KeyLostOrDamaged || rental.KeyLossConfirmedAt is not null;
            var quote = CalculateSettlement(rental, request.KeyLostOrDamaged, now);
            var paidOrders = rental.Payments.Where(IsPaidSettlement).Take(2).ToArray();
            if (paidOrders.Length > 1)
                return Conflict<LockerRentalView>("Hợp đồng có nhiều khoản tất toán đã thu.");
            var paid = paidOrders.SingleOrDefault();
            if (paid is not null)
            {
                if (paid.Order.OrderNumber != request.SettlementOrderNumber ||
                    paid.RentalExpiryAtQuote != RentalExpiry(rental))
                    return Conflict<LockerRentalView>("Khoản đã thu không khớp biên bản trả tủ.");
                quote = SettleAmounts(rental, paid.OverdueFees ?? 0, damaged, now);
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
                initial.RefundReason = DepositReturnReason;
                repo.ResolveRefund(initial, actor, method, request.RefundReference?.Trim(), now);
            }
            if (request.KeyLostOrDamaged && rental.KeyLossConfirmedAt is null)
                MarkKeyLoss(actor, rental, now);

            MarkReturned(actor, rental, now);
            rental.ContractStatus = LockerContractStatus.Terminated;
            rental.CancelledAt = now;
            rental.SettledAt = now;
            rental.SettledByUserId = actor;
            rental.FineAmount = quote.OverdueFees + quote.ForfeitedDeposit;
            rental.Locker.LockerStatus = rental.KeyLossConfirmedAt is not null || !request.LockSafe
                ? LockerStatus.Maintenance : LockerStatus.Available;
            Audit(actor, rental, FormattableString.Invariant(
                $"LockerTerminated:Overdue={quote.OverdueFees}:Refund={quote.RefundAmount}:Forfeited={quote.ForfeitedDeposit}"), now);
            Notify(rental, "Đã trả tủ", quote.ForfeitedDeposit > 0
                ? FormattableString.Invariant($"Hợp đồng {rental.ContractNumber} đã kết thúc. Cọc chìa {quote.ForfeitedDeposit:N0} VND bị mất.")
                : FormattableString.Invariant($"Hợp đồng {rental.ContractNumber} đã kết thúc. Số tiền hoàn cọc: {quote.RefundAmount:N0} VND."), now);
            await repo.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);
}
