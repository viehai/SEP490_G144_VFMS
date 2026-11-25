using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Yoga.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService(
    IMembershipPurchaseRepository repository, IMembershipPaymentGateway gateway, TimeProvider clock,
    IPTPaymentFulfillment? ptPayment = null, ILockerPaymentFulfillment? lockerPayment = null)
    : IMembershipPurchaseService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(PackageOrderTiming.OnlinePaymentMinutes);
    private static readonly TimeSpan CashWindow = TimeSpan.FromHours(PackageOrderTiming.CounterPaymentHours);
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public Task<Result<MembershipPurchaseResponse>> CreateAsync(
        string purchaserId, MembershipPurchaseRequest request, CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var today = LocalDate(clock.GetUtcNow());
            // All payment windows close within 24 hours, so payment can fall on the next local date.
            var latestStart = today.AddDays(MembershipPurchaseMessages.ActivationWindowDays + 1);
            if (request.Items.Any(item => item.ActivationDate is not null))
                return Result<MembershipPurchaseResponse>.Failure(
                    MembershipPurchaseMessages.ActivationAfterPayment, "MEMBERSHIP_ACTIVATION_AFTER_PAYMENT");

            var emails = request.Items.Select(item => NormalizeEmail(item.BeneficiaryEmail))
                .OfType<string>().Distinct().ToArray();
            var users = await repository.LoadCheckoutUsersAsync(purchaserId, emails, ct);
            var purchaser = users.SingleOrDefault(user => user.UserId == purchaserId);
            if (!CanPurchase(purchaser))
                return Result<MembershipPurchaseResponse>.Failure("Tài khoản không thể mua gói tập.", "PURCHASER_NOT_ELIGIBLE", 403);

            var planIds = request.Items.Select(item => item.PlanId).Distinct().ToArray();
            var plans = (await repository.LoadPlansAsync(planIds, ct)).ToDictionary(plan => plan.Id);
            // A yoga-only plan is sold only with a level and while a class it covers can be booked, as in the public catalog.
            var yogaOpen = new Dictionary<int, bool>();
            foreach (var yoga in plans.Values.Where(IsYogaOnly))
                yogaOpen[yoga.Id] = YogaLevel(yoga) is { } level &&
                    await repository.AnyBookableYogaClassAsync(clock.GetUtcNow().UtcDateTime, level, ct);
            var byEmail = users.ToLookup(user => NormalizeEmail(user.Email));
            var order = NewOrder(purchaserId, today,
                request.PaymentMethod == MembershipPaymentMethods.Cash ? OrderChannel.Counter : OrderChannel.Online);

            foreach (var item in request.Items)
            {
                var email = NormalizeEmail(item.BeneficiaryEmail);
                var beneficiary = email is null ? purchaser : FindBeneficiary(byEmail[email]);
                if (!CanPurchase(beneficiary))
                    return Result<MembershipPurchaseResponse>.Failure(
                        "Người thụ hưởng phải có tài khoản Khách hàng hoặc Hội viên đang hoạt động.", "BENEFICIARY_NOT_ELIGIBLE");

                if (!plans.TryGetValue(item.PlanId, out var plan) || !IsPurchasable(plan, latestStart) ||
                    (IsYogaOnly(plan) && !yogaOpen[plan.Id]))
                    return Result<MembershipPurchaseResponse>.Failure(
                        "Một hoặc nhiều gói tập không còn mở bán hoặc chưa có cấu hình thanh toán hợp lệ.", "PLAN_NOT_PURCHASABLE");

                order.Items.Add(NewOrderItem(plan, beneficiary!.UserId, startDate: null));
            }

            return await SavePendingOrderAsync(order, ct);
        }, ct);

    public static Order NewOrder(string purchaserId, DateOnly today, OrderChannel channel) => new()
    {
        PurchaserUserId = purchaserId,
        OrderNumber = $"ORD-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}",
        Channel = channel
    };

    public static OrderItem NewOrderItem(Package plan, string beneficiaryId, DateOnly? startDate) => new()
    {
        PackageId = plan.Id,
        BeneficiaryUserId = beneficiaryId,
        UnitPriceSnapshot = plan.Price,
        PackageNameSnapshot = plan.Name,
        ActivationStartDate = startDate,
        ComponentTermsSnapshot = JsonSerializer.Serialize(plan.Components.OrderBy(component => component.Id).Select(ToTerms))
    };

    private async Task<Result<MembershipPurchaseResponse>> SavePendingOrderAsync(Order order, CancellationToken ct)
    {
        var subtotal = order.Items.Sum(item => item.UnitPriceSnapshot);
        if (subtotal > 9_999_999_999_999_999m)
            return Result<MembershipPurchaseResponse>.Failure("Tổng tiền vượt giới hạn thanh toán.", "ORDER_AMOUNT_INVALID");

        var online = order.Channel == OrderChannel.Online;
        var qr = online ? gateway.CreateVietQr(order.OrderNumber, subtotal) : null;
        if (online && qr is null)
            return Result<MembershipPurchaseResponse>.Failure("Thanh toán VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);

        order.PaymentTransaction = new PaymentTransaction
        {
            UserId = order.PurchaserUserId,
            GatewayName = online ? "SePay" : "Cash",
            PaymentMethod = online ? PaymentMethod.VietQR : PaymentMethod.Cash,
            AmountDue = subtotal
        };
        repository.Add(order);
        await repository.SaveChangesAsync(ct);
        return Result<MembershipPurchaseResponse>.Success(ToResponse(order, OrderStatus.Pending, qr), 201);
    }

    public async Task<Result<MembershipPurchaseResponse>> GetAsync(string purchaserId, string orderNumber, CancellationToken ct)
    {
        var order = await repository.FindOrderAsync(orderNumber, purchaserId, forUpdate: false, ct, membershipOnly: true);
        if (order?.PaymentTransaction is not { } payment || order.Items.Count == 0)
            return OrderNotFound<MembershipPurchaseResponse>();

        var status = order.OrderStatus;
        if (status == OrderStatus.Pending && clock.GetUtcNow() >= ExpiresAt(order))
            status = OrderStatus.Expired;
        var qr = status == OrderStatus.Pending && payment.PaymentMethod == PaymentMethod.VietQR && payment.AmountDue > 0
            ? gateway.CreateVietQr(order.OrderNumber, payment.AmountDue)
            : null;
        return Result<MembershipPurchaseResponse>.Success(ToResponse(order, status, qr));
    }

    public async Task<Result<SepayWebhookResponse>> ConfirmSepayAsync(
        string authorization, string timestamp, string signature, byte[] rawBody, CancellationToken ct)
    {
        var verified = gateway.Verify(authorization, timestamp, signature, rawBody, clock.GetUtcNow());
        if (!verified.IsSuccess)
            return Result<SepayWebhookResponse>.Failure(verified.ErrorMessage!, verified.ErrorCode!, verified.StatusCode);

        var transfer = verified.Data!;
        var renewalPurchaserId = await repository.FindRenewalPurchaserAsync(transfer.OrderNumber, ct);
        return await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            // Renewal creation and callbacks lock the member before inspecting renewal orders.
            if (renewalPurchaserId is not null)
                await repository.LockUsersAsync([renewalPurchaserId], ct);

            var order = await repository.FindOrderAsync(transfer.OrderNumber, purchaserId: null, forUpdate: true, ct);
            if (order?.PaymentTransaction is not { PaymentMethod: PaymentMethod.VietQR, GatewayName: "SePay" } payment)
                return OrderNotFound<SepayWebhookResponse>();

            if (order.OrderStatus == OrderStatus.Paid && payment.GatewayTransactionReference == transfer.TransactionId)
                return Result<SepayWebhookResponse>.Success(new(true));

            if (order.Items.Count == 0)
            {
                var lockerResult = lockerPayment is null ? null : await lockerPayment.TryFulfillAsync(order, transfer, ct);
                return lockerResult ?? OrderNotFound<SepayWebhookResponse>();
            }

            var ptResult = ptPayment is null ? null : await ptPayment.TryFulfillAsync(order, transfer, ct);
            if (ptResult is not null)
                return ptResult;

            // A timely transfer can be delivered after JOB-01 has expired the QR.
            var awaitingSettlement = (order.OrderStatus, payment.PaymentStatus) is
                (OrderStatus.Pending, PaymentStatus.Pending) or (OrderStatus.Expired, PaymentStatus.Cancelled);
            var createdSecond = DateTimeOffset.FromUnixTimeSeconds(Utc(order.CreatedAt).ToUnixTimeSeconds());
            if (!awaitingSettlement ||
                !HasSafeVoucherSettlement(order) ||
                payment.AmountDue != transfer.Amount ||
                transfer.TransferredAt < createdSecond ||
                transfer.TransferredAt >= ExpiresAt(order) ||
                await repository.SepayReferenceUsedAsync(transfer.TransactionId, ct))
                return PaymentRejected<SepayWebhookResponse>();

            if (renewalPurchaserId is not null && !await PrepareRenewalFulfillmentAsync(order, ct))
                return PaymentRejected<SepayWebhookResponse>();

            await FulfillAsync(order, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }, ct);
    }

    public Task<Result<MembershipPurchaseResponse>> ConfirmCashAsync(
        string staffId, string orderNumber, decimal amount, string? ipAddress, CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var staff = (await repository.LoadCheckoutUsersAsync(staffId, [], ct)).SingleOrDefault();
            if (!IsActive(staff) || !HasRole(staff!, UserRole.Staff))
                return Result<MembershipPurchaseResponse>.Failure("Cần quyền nhân viên lễ tân.", "CASH_CONFIRMATION_FORBIDDEN", 403);

            var order = await repository.FindOrderAsync(orderNumber, purchaserId: null, forUpdate: true, ct, membershipOnly: true);
            if (order?.PaymentTransaction is not { PaymentMethod: PaymentMethod.Cash } payment || order.Items.Count == 0 ||
                order.Purchaser.GymId != staff!.GymId)
                return OrderNotFound<MembershipPurchaseResponse>();

            if (order.OrderStatus == OrderStatus.Paid && payment.AmountReceived == amount)
                return Result<MembershipPurchaseResponse>.Success(ToResponse(order, OrderStatus.Paid));

            var now = clock.GetUtcNow();
            if (order.OrderStatus != OrderStatus.Pending || payment.PaymentStatus != PaymentStatus.Pending ||
                now >= ExpiresAt(order) || amount <= 0 || payment.AmountDue != amount ||
                !HasSafeVoucherSettlement(order))
                return Result<MembershipPurchaseResponse>.Failure(
                    "Kiểm tra số tiền đã nhận và hạn thanh toán của đơn.", "CASH_PAYMENT_REJECTED", 409);

            var audit = AuditedWriter.Audit(staffId, AuditActionType.Update, nameof(PaymentTransaction), ipAddress,
                oldValue: nameof(PaymentStatus.Pending),
                newValue: JsonSerializer.Serialize(new { Status = nameof(PaymentStatus.Paid), AmountReceived = amount }));
            audit.EntityId = payment.Id;
            repository.Add(audit);
            await FulfillAsync(order, amount, now, gatewayReference: null, ct);
            return Result<MembershipPurchaseResponse>.Success(ToResponse(order, OrderStatus.Paid));
        }, ct);

    public Task ExpireOrdersAsync(CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var expired = await repository.LoadExpiredOrdersAsync(now - OnlineWindow, now - CashWindow, ct);
            foreach (var order in expired)
            {
                order.OrderStatus = OrderStatus.Expired;
                order.PaymentTransaction!.PaymentStatus = PaymentStatus.Cancelled;
                if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } reservation)
                    reservation.RedemptionStatus = VoucherRedemptionStatus.Released;
            }
            await repository.SaveChangesAsync(ct);
            return expired.Count;
        }, ct);

    private async Task FulfillAsync(
        Order order, decimal amount, DateTimeOffset transferredAt, string? gatewayReference, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = LocalDate(now);
        var userIds = order.Items.Select(item => item.BeneficiaryUserId).Distinct().ToArray();
        var beneficiaries = await repository.LockUsersAsync(userIds, ct);
        var active = new HashSet<string>();
        var freezePolicy = await repository.GetPurchaseFreezePolicyAsync(ct);

        foreach (var item in order.Items.OrderBy(item => item.Id))
        {
            var terms = ReadComponentTerms(item);
            // Legacy pending orders may predate the terms snapshot. Save it before deferring the service term.
            if (item.ActivationStartDate is null && item.ComponentTermsSnapshot is null)
                item.ComponentTermsSnapshot = JsonSerializer.Serialize(terms);

            foreach (var component in terms)
            {
                var membership = new MemberMembership
                {
                    PackageComponentId = component.Id,
                    MemberUserId = item.BeneficiaryUserId,
                    MembershipStatus = MembershipStatus.AwaitingActivation,
                    GrantedFreezeAllowanceDays = ResolveFreezeAllowance(component.Type, component.Days ?? component.DurationDays,
                        item.Package?.FreezeAllowanceOverrideDays),
                    RemainingSessionCount = component.Type == ComponentType.PT ? component.SessionCount : null
                };
                if (component.Type == ComponentType.GYM)
                {
                    var config = item.Package?.Components.FirstOrDefault(row => row.Id == component.Id);
                    membership.GrantedFreezeCount = config?.MaxFreezeCount ?? freezePolicy?.DefaultMaxFreezeCount ?? 0;
                    membership.GrantedMinFreezeDays = config?.MinFreezeDaysPerRequest ?? freezePolicy?.MinFreezeDaysPerRequest;
                    membership.GrantedMaxFreezeDays = config?.MaxFreezeDaysPerRequest ?? freezePolicy?.DefaultMaxFreezeDays;
                    membership.GrantedFreezeAllowanceDays = null;
                }
                // Pre-existing checkout dates and renewals already have a start date on the order line.
                if (item.ActivationStartDate is { } requested)
                {
                    var start = item.RenewedFromMembershipId is null && requested < today ? today : requested;
                    SetMembershipTerm(membership, component, start, today);
                }
                item.MemberMemberships.Add(membership);
                if (membership.MembershipStatus == MembershipStatus.Active)
                    active.Add(item.BeneficiaryUserId);
            }
        }
        await PromoteAsync(beneficiaries.Where(user => active.Contains(user.UserId)), ct);

        RecordPaidOrder(order, amount, transferredAt, gatewayReference, now);
        await repository.SaveChangesAsync(ct);
    }

    internal static int? ResolveFreezeAllowance(ComponentType type, int? durationDays, int? planOverrideDays) =>
        type switch
        {
            ComponentType.YOGA => durationDays == 7 ? 0 : planOverrideDays ?? 0,
            _ => null
        };

    public static void RecordPaidOrder(Order order, decimal amount, DateTimeOffset transferredAt,
        string? gatewayReference, DateTimeOffset now)
    {
        var payment = order.PaymentTransaction!;
        payment.PaymentStatus = PaymentStatus.Paid;
        payment.AmountReceived = amount;
        payment.GatewayTransactionReference = gatewayReference;
        payment.PaidAt = transferredAt.UtcDateTime;
        payment.Invoice = new Invoice
        {
            InvoiceNumber = FormattableString.Invariant($"INV-{LocalDate(now):yyyyMMdd}-{order.Id:D4}"),
            IssuedAt = now.UtcDateTime
        };
        order.OrderStatus = OrderStatus.Paid;
        order.CompletedAt = now.UtcDateTime;
        if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } redemption)
        {
            redemption.RedemptionStatus = VoucherRedemptionStatus.Confirmed;
            redemption.RedeemedAt = transferredAt.UtcDateTime;
        }
    }

    private async Task PromoteAsync(IEnumerable<ApplicationUser> users, CancellationToken ct)
    {
        var candidates = users.Where(user => IsActive(user) && !HasRole(user, UserRole.Member))
            .SelectMany(user => user.UserRoles.Where(role => role.Role.RoleName == nameof(UserRole.Customer))).ToArray();
        if (candidates.Length == 0)
            return;
        var memberRole = await repository.GetMemberRoleAsync(ct)
            ?? throw new InvalidOperationException("The Member role is missing.");
        foreach (var role in candidates)
        {
            role.RoleId = memberRole.Id;
            role.Role = memberRole;
        }
    }

    private MembershipPurchaseResponse ToResponse(Order order, OrderStatus status, VietQrPaymentResponse? qr = null)
    {
        var payment = order.PaymentTransaction!;
        var failed = status is OrderStatus.Expired or OrderStatus.Cancelled || payment.PaymentStatus == PaymentStatus.Failed;
        var onlineFailure = failed && payment.PaymentMethod == PaymentMethod.VietQR;
        var subtotal = order.Items.Sum(item => item.UnitPriceSnapshot);
        var redemption = order.VoucherRedemption;
        var applied = redemption is { VoucherCodeSnapshot: { } code, VoucherNameSnapshot: { } name } &&
            (redemption.RedemptionStatus != VoucherRedemptionStatus.Released ||
                order.OrderStatus is OrderStatus.Expired or OrderStatus.Cancelled && redemption.DiscountAmount > 0)
            ? new AppliedMembershipVoucherResponse(code, name, redemption.DiscountAmount, redemption.RedemptionStatus.ToString())
            : null;
        return new(order.OrderNumber, status.ToString(),
            (status == OrderStatus.Expired ? PaymentStatus.Cancelled : payment.PaymentStatus).ToString(),
            payment.PaymentMethod.ToString(), Utc(order.CreatedAt), ExpiresAt(order), clock.GetUtcNow(),
            subtotal, payment.AmountDue,
            order.Items.OrderBy(item => item.Id).Select(item => new MembershipPurchaseItemResponse(item.PackageId,
                item.PackageNameSnapshot, item.BeneficiaryUserId == order.PurchaserUserId, item.ActivationStartDate,
                item.UnitPriceSnapshot, item.MemberMemberships.Any(membership =>
                    membership.MembershipStatus == MembershipStatus.AwaitingActivation))).ToArray(),
            qr, payment.Invoice?.InvoiceNumber,
            status == OrderStatus.Paid ? MembershipPurchaseMessages.PaidCode : onlineFailure ? MembershipPurchaseMessages.FailedCode : null,
            status == OrderStatus.Paid ? MembershipPurchaseMessages.Paid : onlineFailure ? MembershipPurchaseMessages.Failed
                : failed ? "Đơn thanh toán tại quầy đã hết hiệu lực." : null,
            Math.Max(0, subtotal - payment.AmountDue), applied);
    }

    private static bool IsYogaOnly(Package plan) =>
        plan.Components.All(component => component.ComponentType != ComponentType.GYM);

    private static string? YogaLevel(Package plan) =>
        plan.Components.FirstOrDefault(component => component.ComponentType == ComponentType.YOGA)?.YogaDifficultyLevel;

    private static bool IsPurchasable(Package plan, DateOnly latestStart) =>
        plan.PackageStatus == PackageStatus.Active && plan.Price > 0 && decimal.Truncate(plan.Price) == plan.Price &&
        plan.Components.Any(component => component.ComponentType is ComponentType.GYM or ComponentType.YOGA) &&
        plan.Components.All(component => IsValidComponent(component, latestStart));

    private static ComponentTerms[] ReadComponentTerms(OrderItem item) =>
        item.ComponentTermsSnapshot is { } snapshot
            ? JsonSerializer.Deserialize<ComponentTerms[]>(snapshot)!
            : item.Package.Components.OrderBy(component => component.Id).Select(ToTerms).ToArray();

    internal static (int? Days, int? Months) PurchasedDuration(
        string? snapshot, int componentId, int? currentDays, int? currentMonths)
    {
        if (snapshot is null)
            return (currentDays, currentMonths);

        try
        {
            var terms = JsonSerializer.Deserialize<ComponentTerms[]>(snapshot)?
                .SingleOrDefault(component => component.Id == componentId);
            return (terms?.Days ?? terms?.DurationDays, terms?.Months ?? terms?.DurationMonths);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static bool IsValidComponent(PackageComponent component, DateOnly latestStart) =>
        TermFits(component.DurationDays, component.DurationMonths, latestStart) &&
        (component.ComponentType != ComponentType.YOGA || YogaRules.IsDifficulty(component.YogaDifficultyLevel)) &&
        (component.ComponentType != ComponentType.PT || component.SessionCount is > 0);

    private static bool TermFits(int? days, int? months, DateOnly start) => days is > 0
        ? days.Value - 1 <= DateOnly.MaxValue.DayNumber - start.DayNumber
        : months is > 0 && months.Value <= (DateOnly.MaxValue.Year - start.Year) * 12 + 12 - start.Month;

    private static bool CanPurchase(ApplicationUser? user) =>
        IsActive(user) && (HasRole(user!, UserRole.Customer) || HasRole(user!, UserRole.Member));

    private static bool IsActive(ApplicationUser? user) =>
        user is { AccountStatus: AccountStatus.Active, IsBanned: false };

    private static bool HasRole(ApplicationUser user, UserRole role) =>
        user.UserRoles.Any(link => link.Role.RoleName == role.ToString());

    private static DateOnly LocalDate(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime);
    private static DateTimeOffset Utc(DateTime time) => new(time, TimeSpan.Zero);
    private static DateTimeOffset ExpiresAt(Order order) =>
        Utc(order.CreatedAt) + (order.Channel == OrderChannel.Online ? OnlineWindow : CashWindow);

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static ApplicationUser? FindBeneficiary(IEnumerable<ApplicationUser> matches) =>
        matches.Take(2).ToArray() is [var user] ? user : null;

    private static ComponentTerms ToTerms(PackageComponent component) => new(
        component.Id, component.ComponentType, component.DurationDays, component.DurationMonths, component.SessionCount);

    private static DateOnly EndDate(DateOnly start, ComponentTerms component)
    {
        var days = component.Days ?? component.DurationDays;
        var months = component.Months ?? component.DurationMonths;
        return days is > 0 ? start.AddDays(days.Value - 1) : start.AddMonths(months!.Value).AddDays(-1);
    }

    private static Result<T> OrderNotFound<T>() =>
        Result<T>.Failure("Không tìm thấy đơn hàng.", "MEMBERSHIP_ORDER_NOT_FOUND", 404);

    private static Result<T> PaymentRejected<T>() =>
        Result<T>.Failure(MembershipPurchaseMessages.Failed, MembershipPurchaseMessages.FailedCode, 409);

    private sealed record ComponentTerms(int Id, ComponentType Type, int? Days, int? Months, int? SessionCount)
    {
        // Older checkout snapshots used the entity's property names for the duration.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DurationDays { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DurationMonths { get; init; }
    }
}
