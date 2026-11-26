using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService
{
    private const string PendingGateway = "PendingChoice";
    // An unselected gateway distinguishes deferred checkouts from legacy issued cash/QR orders.
    private static bool ChoosingPayment(PaymentTransaction charge) => charge.PaymentStartedAt is null &&
        (NeedsProceed(charge) || charge.GatewayName == PendingGateway);

    private static decimal Discount(PaymentTransaction charge) => charge.Order.VoucherRedemption?.DiscountAmount ?? 0;
    private static decimal DiscountableFee(PaymentTransaction charge) => charge.LockerPurpose == LockerPaymentPurpose.Rental
        ? charge.LockerRentalContract!.RentalPrice : charge.AmountDue + Discount(charge) - (charge.OverdueFees ?? 0);

    private static void ReleaseVoucher(PaymentTransaction charge)
    {
        if (charge.Order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } voucher)
            voucher.RedemptionStatus = VoucherRedemptionStatus.Released;
    }

    public Task<Result<LockerCheckout>> ChoosePayment(string actor, UserRole role, string number, LockerPaymentRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            var user = await Actor(actor, role, ct);
            if (user is null) return Forbidden<LockerCheckout>();
            var method = ParseCollection(input.PaymentMethod);
            if (method is null) return Invalid<LockerCheckout>("Chọn Cash hoặc VietQR.");
            var code = string.IsNullOrWhiteSpace(input.VoucherCode) ? null : VoucherRules.NormalizeCode(input.VoucherCode);
            if (code?.Length > 80) return Invalid<LockerCheckout>("Mã voucher tối đa 80 ký tự.");
            var voucher = code is null ? null : await commerce.FindVoucherForUpdateAsync(code, ct);
            var order = await commerce.FindOrderAsync(number, role == UserRole.Member ? actor : null, forUpdate: true, ct);
            var charge = order is null ? null : await LoadCharge(order.Id, ct);
            if (charge is null || charge.LockerRentalContract!.Locker.LockerZone.GymId != user.GymId)
                return Missing<LockerCheckout>();
            var now = Now;
            if (order!.OrderStatus == OrderStatus.Paid)
                return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime));
            if (!Pending(charge, now.UtcDateTime) || PaymentDeadline(charge) <= now.UtcDateTime ||
                charge.LockerPurpose is not (LockerPaymentPurpose.Rental or LockerPaymentPurpose.Renewal))
                return Conflict<LockerCheckout>("Đơn không còn chờ chọn thanh toán.");
            if (!ChoosingPayment(charge))
                return charge.PaymentMethod == method && string.Equals(order.VoucherRedemption?.VoucherCodeSnapshot, code, StringComparison.OrdinalIgnoreCase)
                    ? Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime, Qr(charge)))
                    : Conflict<LockerCheckout>("Đơn đã chốt phương thức và voucher. Hủy đơn còn chờ nếu cần chọn lại.");
            if (NeedsProceed(charge) && ProceedDeadline(charge) <= now.UtcDateTime)
                return Conflict<LockerCheckout>("Đã quá 10 phút để chọn thanh toán. Vui lòng thuê lại.");

            VoucherRedemption? redemption = null;
            if (code is not null)
            {
                if (voucher is null) return Result<LockerCheckout>.Failure("Không tìm thấy mã voucher.", "VOUCHER_NOT_FOUND");
                var member = charge.LockerRentalContract.Customer;
                var capacity = await commerce.ReadVoucherCapacityAsync(voucher.Id, member, ct);
                var check = VoucherCheckoutRules.Check(voucher, member.CreatedAt, now.UtcDateTime, capacity, DiscountableFee(charge));
                if (!check.IsSuccess) return Result<LockerCheckout>.Failure(check.ErrorMessage!, check.ErrorCode!, check.StatusCode);
                redemption = new VoucherRedemption
                {
                    VoucherId = voucher.Id,
                    VoucherGrantId = voucher.Audience == VoucherAudience.AssignedUsers ? capacity.AvailableGrantId : null,
                    VoucherCodeSnapshot = voucher.Code, VoucherNameSnapshot = voucher.Name,
                    DiscountAmount = check.Data, RedeemedAt = now.UtcDateTime
                };
            }
            var due = charge.AmountDue - (redemption?.DiscountAmount ?? 0);
            var qr = method == PaymentMethod.VietQR && due > 0 ? gateway.CreateVietQr(number, due) : null;
            if (method == PaymentMethod.VietQR && due > 0 && qr is null) return PaymentUnavailable();
            if (due == 0 && !await CanFulfill(charge, now.UtcDateTime, now.UtcDateTime, ct, allowChoice: true))
                return Conflict<LockerCheckout>("Hợp đồng không còn đủ điều kiện hoàn tất thanh toán.");
            order.VoucherRedemption = redemption;
            charge.AmountDue = due;
            charge.PaymentStartedAt = now.UtcDateTime;
            charge.PaymentMethod = due == 0 ? PaymentMethod.NoCharge : method.Value;
            charge.GatewayName = due == 0 ? "NoCharge" : method == PaymentMethod.VietQR ? "SePay" : "Cash";
            if (method == PaymentMethod.Cash)
            {
                order.Channel = OrderChannel.Counter;
                charge.HoldExpiresAt = PaymentDeadline(charge);
            }
            if (due == 0)
                await ApplyPayment(charge, 0, now, null, now, actor, ct);
            else
            {
                Audit(actor, charge.LockerRentalContract, $"LockerPaymentChosen:{charge.PaymentMethod}:{number}", now.UtcDateTime);
                await repo.SaveChangesAsync(ct);
            }
            return Result<LockerCheckout>.Success(Checkout(charge, now.UtcDateTime, qr));
        }, ct);
}
