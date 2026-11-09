using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService(
    IMembershipPurchaseRepository repository, IMembershipPaymentGateway gateway, TimeProvider clock)
    : IMembershipPurchaseService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CashWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public Task<Result<MembershipPurchaseResponse>> CreateAsync(
        string purchaserId, MembershipPurchaseRequest request, CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var today = LocalDate(clock.GetUtcNow());
            if (request.Items.Any(item => item.ActivationDate is { } date && (date < today || date > today.AddDays(30))))
                return Result<MembershipPurchaseResponse>.Failure(
                    MembershipPurchaseMessages.ActivationDate, MembershipPurchaseMessages.ActivationDateCode);

            var emails = request.Items.Select(item => NormalizeEmail(item.BeneficiaryEmail))
                .OfType<string>().Distinct().ToArray();
            var users = await repository.LoadCheckoutUsersAsync(purchaserId, emails, ct);
            var purchaser = users.SingleOrDefault(user => user.UserId == purchaserId);
            if (!CanPurchase(purchaser))
                return Result<MembershipPurchaseResponse>.Failure("Tài khoản không thể mua gói tập.", "PURCHASER_NOT_ELIGIBLE", 403);

            var planIds = request.Items.Select(item => item.PlanId).Distinct().ToArray();
            var plans = (await repository.LoadPlansAsync(planIds, ct)).ToDictionary(plan => plan.Id);
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

                if (!plans.TryGetValue(item.PlanId, out var plan) || !IsPurchasable(plan, today.AddDays(30)))
                    return Result<MembershipPurchaseResponse>.Failure(
                        "Một hoặc nhiều gói tập không còn mở bán hoặc chưa có cấu hình thanh toán hợp lệ.", "PLAN_NOT_PURCHASABLE");

                order.Items.Add(NewOrderItem(plan, beneficiary!.UserId, item.ActivationDate));
            }

            return await SavePendingOrderAsync(order, ct);
        }, ct);

    private static Order NewOrder(string purchaserId, DateOnly today, OrderChannel channel) => new()
    {
        PurchaserUserId = purchaserId,
        OrderNumber = $"ORD-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}",
        Channel = channel
    };

    private static OrderItem NewOrderItem(Package plan, string beneficiaryId, DateOnly? startDate) => new()
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
        var order = await repository.FindOrderAsync(orderNumber, purchaserId, forUpdate: false, ct);
        if (order?.PaymentTransaction is not { } payment)
            return OrderNotFound<MembershipPurchaseResponse>();

        var status = order.OrderStatus;
        if (status == OrderStatus.Pending && clock.GetUtcNow() >= ExpiresAt(order))
            status = OrderStatus.Expired;
        var qr = status == OrderStatus.Pending && payment.PaymentMethod == PaymentMethod.VietQR
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

            // A timely transfer can be delivered after JOB-01 has expired the QR.
            var awaitingSettlement = (order.OrderStatus, payment.PaymentStatus) is
                (OrderStatus.Pending, PaymentStatus.Pending) or (OrderStatus.Expired, PaymentStatus.Cancelled);
            var createdSecond = DateTimeOffset.FromUnixTimeSeconds(Utc(order.CreatedAt).ToUnixTimeSeconds());
            if (!awaitingSettlement ||
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

            var order = await repository.FindOrderAsync(orderNumber, purchaserId: null, forUpdate: true, ct);
            if (order?.PaymentTransaction is not { PaymentMethod: PaymentMethod.Cash } payment ||
                order.Purchaser.GymId != staff!.GymId)
                return OrderNotFound<MembershipPurchaseResponse>();

            if (order.OrderStatus == OrderStatus.Paid && payment.AmountReceived == amount)
                return Result<MembershipPurchaseResponse>.Success(ToResponse(order, OrderStatus.Paid));

            var now = clock.GetUtcNow();
            if (order.OrderStatus != OrderStatus.Pending || payment.PaymentStatus != PaymentStatus.Pending ||
                now >= ExpiresAt(order) || payment.AmountDue != amount)
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

    public Task AdvanceMembershipsAsync(CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var today = LocalDate(clock.GetUtcNow());
            var due = await repository.LoadDueMembershipsAsync(today, ct);
            foreach (var membership in due)
                membership.MembershipStatus = membership.EndDate < today ? MembershipStatus.Expired : MembershipStatus.Active;

            var activatedUsers = due.Where(membership => membership.MembershipStatus == MembershipStatus.Active)
                .Select(membership => membership.MemberUserId).Distinct().ToArray();
            if (activatedUsers.Length > 0)
                await PromoteAsync(await repository.LockUsersAsync(activatedUsers, ct), ct);

            await repository.SaveChangesAsync(ct);
            return due.Count;
        }, ct);

    private async Task FulfillAsync(
        Order order, decimal amount, DateTimeOffset transferredAt, string? gatewayReference, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = LocalDate(now);
        var userIds = order.Items.Select(item => item.BeneficiaryUserId).Distinct().ToArray();
        var beneficiaries = await repository.LockUsersAsync(userIds, ct);
        var memberships = await repository.LoadCurrentMembershipsAsync(userIds, today, ct);
        var endDates = memberships.GroupBy(membership => (membership.MemberUserId, membership.PackageComponent.ComponentType))
            .ToDictionary(group => group.Key, group => group.Max(membership => membership.EndDate));
        var active = new HashSet<string>();

        foreach (var item in order.Items.OrderBy(item => item.Id))
        {
            foreach (var component in ReadComponentTerms(item))
            {
                var start = item.ActivationStartDate is { } requested && requested > today ? requested : today;
                var key = (item.BeneficiaryUserId, component.Type);
                if (item.RenewedFromMembershipId is not null && item.ActivationStartDate is { } renewalStart)
                    start = renewalStart;
                else if (endDates.TryGetValue(key, out var lastEnd) && lastEnd >= start)
                    start = lastEnd.AddDays(1);

                var end = EndDate(start, component);
                var status = end < today ? MembershipStatus.Expired
                    : start > today ? MembershipStatus.Scheduled : MembershipStatus.Active;
                if (!endDates.TryGetValue(key, out var latestEnd) || end > latestEnd)
                    endDates[key] = end;
                item.MemberMemberships.Add(new MemberMembership
                {
                    PackageComponentId = component.Id,
                    MemberUserId = item.BeneficiaryUserId,
                    StartDate = start,
                    EndDate = end,
                    MembershipStatus = status,
                    RemainingSessionCount = component.Type == ComponentType.PT ? component.SessionCount : null
                });
                if (status == MembershipStatus.Active)
                    active.Add(item.BeneficiaryUserId);
            }
        }
        await PromoteAsync(beneficiaries.Where(user => active.Contains(user.UserId)), ct);

        var payment = order.PaymentTransaction!;
        payment.PaymentStatus = PaymentStatus.Paid;
        payment.AmountReceived = amount;
        payment.GatewayTransactionReference = gatewayReference;
        payment.PaidAt = transferredAt.UtcDateTime;
        payment.Invoice = new Invoice
        {
            InvoiceNumber = FormattableString.Invariant($"INV-{today:yyyyMMdd}-{order.Id:D4}"),
            IssuedAt = now.UtcDateTime
        };
        order.OrderStatus = OrderStatus.Paid;
        order.CompletedAt = now.UtcDateTime;
        await repository.SaveChangesAsync(ct);
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
        return new(order.OrderNumber, status.ToString(),
            (status == OrderStatus.Expired ? PaymentStatus.Cancelled : payment.PaymentStatus).ToString(),
            payment.PaymentMethod.ToString(), Utc(order.CreatedAt), ExpiresAt(order), clock.GetUtcNow(),
            order.Items.Sum(item => item.UnitPriceSnapshot), payment.AmountDue,
            order.Items.OrderBy(item => item.Id).Select(item => new MembershipPurchaseItemResponse(item.PackageId,
                item.PackageNameSnapshot, item.BeneficiaryUserId == order.PurchaserUserId, item.ActivationStartDate,
                item.UnitPriceSnapshot)).ToArray(),
            qr, payment.Invoice?.InvoiceNumber,
            status == OrderStatus.Paid ? MembershipPurchaseMessages.PaidCode : onlineFailure ? MembershipPurchaseMessages.FailedCode : null,
            status == OrderStatus.Paid ? MembershipPurchaseMessages.Paid : onlineFailure ? MembershipPurchaseMessages.Failed
                : failed ? "Đơn thanh toán tại quầy đã hết hiệu lực." : null);
    }

    private static bool IsPurchasable(Package plan, DateOnly latestStart) =>
        plan.PackageStatus == PackageStatus.Active && plan.Price > 0 && decimal.Truncate(plan.Price) == plan.Price &&
        plan.Components.Any(component => component.ComponentType == ComponentType.GYM) &&
        plan.Components.All(component => IsValidComponent(component, latestStart));

    private static ComponentTerms[] ReadComponentTerms(OrderItem item) =>
        item.ComponentTermsSnapshot is { } snapshot
            ? JsonSerializer.Deserialize<ComponentTerms[]>(snapshot)!
            : item.Package.Components.OrderBy(component => component.Id).Select(ToTerms).ToArray();

    private static bool IsValidComponent(PackageComponent component, DateOnly latestStart)
    {
        var fits = component.DurationDays is > 0
            ? component.DurationDays - 1 <= DateOnly.MaxValue.DayNumber - latestStart.DayNumber
            : component.DurationMonths is > 0 && component.DurationMonths <= (9999 - latestStart.Year) * 12 + 12 - latestStart.Month;
        return fits && (component.ComponentType != ComponentType.PT || component.SessionCount is > 0);
    }

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
