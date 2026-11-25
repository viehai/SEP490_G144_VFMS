using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService
{
    public async Task<Result<MembershipPurchaseResponse>> ApplyVoucherAsync(
        string purchaserId, string orderNumber, ApplyMembershipVoucherRequest request, CancellationToken ct)
    {
        var validation = await new ApplyMembershipVoucherRequestValidator().ValidateAsync(request, ct);
        if (!validation.IsValid)
            return VoucherError(string.Join(" ", validation.Errors.Select(error => error.ErrorMessage).Distinct()), "VOUCHER_CODE_REQUIRED");
        if (!await CanAlterCheckoutAsync(purchaserId, ct))
            return PurchaserForbidden();

        var code = VoucherRules.NormalizeCode(request.VoucherCode!);
        return await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var voucher = await repository.FindVoucherForUpdateAsync(code, ct);
            var order = await repository.FindOrderAsync(orderNumber, purchaserId, forUpdate: true, ct, membershipOnly: true);
            if (order?.PaymentTransaction is not { } payment)
                return OrderNotFound<MembershipPurchaseResponse>();
            if (!CanAlterOrder(order))
                return OrderNotPending();
            if (order.Items.Count == 0)
                return VoucherError("Đơn hàng phải có ít nhất một gói tập.", "ORDER_EMPTY");

            if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } existing)
            {
                // Repeated Apply keeps the transaction snapshot even if its campaign changed afterwards.
                return existing.VoucherCodeSnapshot is { } snapshot && VoucherRules.NormalizeCode(snapshot) == code
                    ? AppliedResponse(order)
                    : VoucherError("Mỗi giao dịch chỉ được sử dụng một voucher. Hãy gỡ voucher hiện tại trước.", "VOUCHER_ALREADY_APPLIED", 409);
            }
            if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Confirmed })
                return OrderNotPending();
            if (voucher is null)
                return VoucherError("Không tìm thấy mã voucher.", "VOUCHER_NOT_FOUND");

            var subtotal = order.Items.Sum(item => item.UnitPriceSnapshot);
            var capacity = await repository.ReadVoucherCapacityAsync(voucher.Id, order.Purchaser, ct);
            var check = VoucherCheckoutRules.Check(voucher, order.Purchaser.CreatedAt, clock.GetUtcNow().UtcDateTime, capacity, subtotal);
            if (!check.IsSuccess) return VoucherError(check.ErrorMessage!, check.ErrorCode!, check.StatusCode);
            var discount = check.Data;
            var due = subtotal - discount;
            var qr = payment.PaymentMethod == PaymentMethod.VietQR && due > 0
                ? gateway.CreateVietQr(order.OrderNumber, due) : null;
            if (payment.PaymentMethod == PaymentMethod.VietQR && due > 0 && qr is null)
                return PaymentUnavailable();

            var redemption = order.VoucherRedemption ??= new VoucherRedemption { Order = order };
            redemption.VoucherId = voucher.Id;
            redemption.VoucherGrantId = voucher.Audience == VoucherAudience.AssignedUsers ? capacity.AvailableGrantId : null;
            redemption.VoucherCodeSnapshot = voucher.Code;
            redemption.VoucherNameSnapshot = voucher.Name;
            redemption.DiscountAmount = discount;
            redemption.RedeemedAt = clock.GetUtcNow().UtcDateTime;
            redemption.RedemptionStatus = VoucherRedemptionStatus.Reserved;
            redemption.IsDeleted = false;
            payment.AmountDue = due;
            await repository.SaveChangesAsync(ct);
            return AppliedResponse(order, qr);
        }, ct);
    }

    public async Task<Result<MembershipPurchaseResponse>> RemoveVoucherAsync(
        string purchaserId, string orderNumber, CancellationToken ct)
    {
        if (!await CanAlterCheckoutAsync(purchaserId, ct))
            return PurchaserForbidden();
        return await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var order = await repository.FindOrderAsync(orderNumber, purchaserId, forUpdate: true, ct, membershipOnly: true);
            if (order?.PaymentTransaction is not { } payment)
                return OrderNotFound<MembershipPurchaseResponse>();
            if (!CanAlterOrder(order))
                return OrderNotPending();
            var subtotal = order.Items.Sum(item => item.UnitPriceSnapshot);
            var qr = payment.PaymentMethod == PaymentMethod.VietQR ? gateway.CreateVietQr(order.OrderNumber, subtotal) : null;
            if (payment.PaymentMethod == PaymentMethod.VietQR && qr is null)
                return PaymentUnavailable();
            if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } redemption)
            {
                redemption.RedemptionStatus = VoucherRedemptionStatus.Released;
                payment.AmountDue = subtotal;
                await repository.SaveChangesAsync(ct);
            }
            return Result<MembershipPurchaseResponse>.Success(ToResponse(order, order.OrderStatus, qr));
        }, ct);
    }

    public async Task<Result<MembershipPurchaseResponse>> CompleteNoChargeAsync(
        string purchaserId, string orderNumber, CancellationToken ct)
    {
        if (!await CanAlterCheckoutAsync(purchaserId, ct))
            return PurchaserForbidden();
        var renewalPurchaser = await repository.FindRenewalPurchaserAsync(orderNumber, ct);
        return await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            if (renewalPurchaser is not null)
                await repository.LockUsersAsync([renewalPurchaser], ct);
            var order = await repository.FindOrderAsync(orderNumber, purchaserId, forUpdate: true, ct, membershipOnly: true);
            if (order?.PaymentTransaction is not { } payment)
                return OrderNotFound<MembershipPurchaseResponse>();
            if (order.OrderStatus == OrderStatus.Paid && payment.PaymentMethod == PaymentMethod.NoCharge)
                return Result<MembershipPurchaseResponse>.Success(ToResponse(order, order.OrderStatus));
            if (!CanAlterOrder(order))
                return OrderNotPending();
            if (payment.AmountDue != 0 || order.Items.Count == 0 || order.VoucherRedemption is not
                { RedemptionStatus: VoucherRedemptionStatus.Reserved } redemption ||
                redemption.DiscountAmount != order.Items.Sum(item => item.UnitPriceSnapshot))
                return VoucherError("Đơn hàng chưa đủ điều kiện hoàn tất không thu tiền.", "NO_CHARGE_NOT_ELIGIBLE", 409);
            if (renewalPurchaser is not null && !await PrepareRenewalFulfillmentAsync(order, ct))
                return PaymentRejected<MembershipPurchaseResponse>();

            // Zero is the actual amount received. No bank transfer or cash receipt is fabricated.
            payment.PaymentMethod = PaymentMethod.NoCharge;
            payment.GatewayName = "NoCharge";
            await FulfillAsync(order, 0, clock.GetUtcNow(), gatewayReference: null, ct);
            return Result<MembershipPurchaseResponse>.Success(ToResponse(order, order.OrderStatus));
        }, ct);
    }

    private async Task<bool> CanAlterCheckoutAsync(string purchaserId, CancellationToken ct) =>
        CanPurchase((await repository.LoadCheckoutUsersAsync(purchaserId, [], ct)).SingleOrDefault(user => user.UserId == purchaserId));

    private bool CanAlterOrder(Order order) => order.OrderStatus == OrderStatus.Pending &&
        order.PaymentTransaction is { PaymentStatus: PaymentStatus.Pending, PaymentMethod: PaymentMethod.Cash or PaymentMethod.VietQR } &&
        clock.GetUtcNow() < ExpiresAt(order);

    private Result<MembershipPurchaseResponse> AppliedResponse(Order order, VietQrPaymentResponse? qr = null)
    {
        qr ??= order.PaymentTransaction is { PaymentMethod: PaymentMethod.VietQR, AmountDue: > 0 } payment
            ? gateway.CreateVietQr(order.OrderNumber, payment.AmountDue) : null;
        return Result<MembershipPurchaseResponse>.Success(ToResponse(order, order.OrderStatus, qr) with
            { MessageCode = "MSG-04", Message = "Áp dụng voucher thành công." });
    }

    private static bool HasSafeVoucherSettlement(Order order) => order.VoucherRedemption switch
    {
        null => true,
        { RedemptionStatus: VoucherRedemptionStatus.Reserved } redemption =>
            redemption.DiscountAmount == order.Items.Sum(item => item.UnitPriceSnapshot) - order.PaymentTransaction!.AmountDue,
        { RedemptionStatus: VoucherRedemptionStatus.Released } => order.OrderStatus == OrderStatus.Pending &&
            order.PaymentTransaction!.AmountDue == order.Items.Sum(item => item.UnitPriceSnapshot),
        _ => false
    };

    private static Result<MembershipPurchaseResponse> VoucherError(string message, string code, int status = 400) =>
        Result<MembershipPurchaseResponse>.Failure(message, code, status);
    private static Result<MembershipPurchaseResponse> OrderNotPending() => VoucherError(
        "Đơn hàng không còn chờ thanh toán hoặc đã hết thời gian thanh toán.", "ORDER_NOT_PENDING", 409);
    private static Result<MembershipPurchaseResponse> PurchaserForbidden() => VoucherError(
        "Chỉ tài khoản Khách hàng hoặc Hội viên đang hoạt động mới có thể quản lý thanh toán.", "PURCHASER_NOT_ELIGIBLE", 403);
    private static Result<MembershipPurchaseResponse> PaymentUnavailable() => VoucherError(
        "Thanh toán VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
}
