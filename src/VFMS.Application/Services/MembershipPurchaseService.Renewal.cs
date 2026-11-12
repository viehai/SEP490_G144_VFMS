using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService
{
    public async Task<Result<IReadOnlyList<MembershipRenewalOption>>> ListRenewalsAsync(
        string memberId, CancellationToken ct)
    {
        var member = (await repository.LoadCheckoutUsersAsync(memberId, [], ct)).SingleOrDefault();
        if (!CanRenew(member))
            return RenewalForbidden<IReadOnlyList<MembershipRenewalOption>>();

        var memberships = await repository.LoadRenewableMembershipsAsync(memberId, membershipId: null, ct);
        if (memberships.Count == 0)
            return Result<IReadOnlyList<MembershipRenewalOption>>.Success([]);

        var now = clock.GetUtcNow();
        var today = LocalDate(now);
        var gym = await repository.GetRenewalPolicyGymAsync(ct);
        var plans = (await repository.LoadPlansAsync(
            memberships.Select(membership => membership.OrderItem.PackageId).Distinct().ToArray(), ct))
            .ToDictionary(plan => plan.Id);
        var orders = await repository.LoadRenewalOrdersAsync(
            memberships.Select(membership => membership.Id).ToArray(), forUpdate: false, ct);
        var current = await repository.LoadCurrentMembershipsAsync([memberId], today, ct);
        var options = memberships.Select(membership => EvaluateRenewal(membership, gym,
            plans.GetValueOrDefault(membership.OrderItem.PackageId),
            orders.Where(order => order.Items.Any(item => item.RenewedFromMembershipId == membership.Id)),
            current, now).Option).ToArray();
        return Result<IReadOnlyList<MembershipRenewalOption>>.Success(options);
    }

    public Task<Result<MembershipPurchaseResponse>> RenewAsync(string memberId, int membershipId, CancellationToken ct) =>
        repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var member = (await repository.LockUsersAsync([memberId], ct)).SingleOrDefault();
            if (!CanRenew(member))
                return RenewalForbidden<MembershipPurchaseResponse>();

            var membership = (await repository.LoadRenewableMembershipsAsync(memberId, membershipId, ct)).SingleOrDefault();
            if (membership is null)
                return MembershipNotFound<MembershipPurchaseResponse>();

            var now = clock.GetUtcNow();
            var today = LocalDate(now);
            var gym = await repository.GetRenewalPolicyGymAsync(ct);
            var plan = (await repository.LoadPlansAsync([membership.OrderItem.PackageId], ct)).SingleOrDefault();
            var orders = await repository.LoadRenewalOrdersAsync([membership.Id], forUpdate: false, ct);
            var current = await repository.LoadCurrentMembershipsAsync([memberId], today, ct);
            var (option, pending) = EvaluateRenewal(membership, gym, plan, orders, current, now);
            if (!option.Eligible)
            {
                var status = option.MessageCode is "RENEWAL_ALREADY_PAID" or "RENEWAL_SCHEDULE_CONFLICT" ? 409
                    : option.MessageCode == "RENEWAL_POLICY_UNAVAILABLE" ? 503 : 400;
                return Result<MembershipPurchaseResponse>.Failure(option.Message!, option.MessageCode!, status);
            }

            if (pending is not null)
            {
                var qr = gateway.CreateVietQr(pending.OrderNumber, pending.PaymentTransaction!.AmountDue);
                return qr is null
                    ? Result<MembershipPurchaseResponse>.Failure("Thanh toán VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503)
                    : Result<MembershipPurchaseResponse>.Success(ToResponse(pending, OrderStatus.Pending, qr));
            }

            var order = NewOrder(memberId, today, OrderChannel.Online);
            var item = NewOrderItem(plan!, memberId, option.RenewalStartDate);
            item.RenewedFromMembershipId = membership.Id;
            order.Items.Add(item);
            return await SavePendingOrderAsync(order, ct);
        }, ct);

    private static (MembershipRenewalOption Option, Order? Pending) EvaluateRenewal(
        MemberMembership membership, Gym? gym, Package? plan, IEnumerable<Order> orders,
        IReadOnlyCollection<MemberMembership> current, DateTimeOffset now)
    {
        var today = LocalDate(now);
        var status = GetCurrentMembershipStatus(membership.MembershipStatus, membership.EndDate, today);
        var option = new MembershipRenewalOption(membership.Id, plan?.Name ?? membership.OrderItem.PackageNameSnapshot,
            status.ToString(), membership.EndDate, null, plan?.Price, false, null, null);
        if (status is MembershipStatus.Frozen or MembershipStatus.Suspended)
            return (option with { MessageCode = MembershipPurchaseMessages.RenewalBlockedCode,
                Message = MembershipPurchaseMessages.RenewalBlocked }, null);

        if (status is not (MembershipStatus.Active or MembershipStatus.Expired) || membership.StartDate > today ||
            membership.EndDate == DateOnly.MaxValue)
            return (option with { MessageCode = "MEMBERSHIP_NOT_RENEWABLE",
                Message = "Trạng thái hiện tại của gói chưa cho phép gia hạn." }, null);

        if (orders.Any(order => order.OrderStatus == OrderStatus.Paid))
            return (option with { MessageCode = "RENEWAL_ALREADY_PAID",
                Message = "Gói hội viên này đã được gia hạn." }, null);

        var pending = orders.FirstOrDefault(order => order.OrderStatus == OrderStatus.Pending &&
            order.PaymentTransaction?.PaymentStatus == PaymentStatus.Pending && now < ExpiresAt(order));
        if (pending is not null)
        {
            var item = pending.Items.Single();
            return (option with { Eligible = true, PlanName = item.PackageNameSnapshot,
                RenewalStartDate = item.ActivationStartDate, Price = item.UnitPriceSnapshot }, pending);
        }

        if (gym is null)
            return (option with { MessageCode = "RENEWAL_POLICY_UNAVAILABLE",
                Message = "Chính sách gia hạn chưa sẵn sàng." }, null);

        var daysUntilExpiry = membership.EndDate.DayNumber - today.DayNumber;
        if (daysUntilExpiry > gym.RenewalAdvanceDays || daysUntilExpiry < -gym.RenewalGraceDays)
            return (option with { MessageCode = "RENEWAL_OUTSIDE_WINDOW",
                Message = "Gói nằm ngoài thời hạn gia hạn. Bạn có thể mua một gói mới." }, null);

        var start = membership.EndDate.AddDays(1);
        if (plan is null || !IsPurchasable(plan, start) ||
            plan.Components.Select(component => component.ComponentType).Distinct().Count() != plan.Components.Count)
            return (option with { MessageCode = "PLAN_NOT_PURCHASABLE",
                Message = "Gói hiện tại không còn hỗ trợ gia hạn." }, null);

        var gymComponent = plan.Components.Single(component => component.ComponentType == ComponentType.GYM);
        if (EndDate(start, ToTerms(gymComponent)) < today)
            return (option with { MessageCode = "RENEWAL_TERM_ELAPSED",
                Message = "Thời gian sử dụng của kỳ gia hạn đã qua. Bạn có thể mua một gói mới." }, null);

        if (HasRenewalOverlap(start, plan.Components.Select(ToTerms), current))
            return (option with { MessageCode = "RENEWAL_SCHEDULE_CONFLICT",
                Message = "Thời gian gia hạn trùng với một gói đã có." }, null);

        return (option with { Eligible = true, RenewalStartDate = start }, null);
    }

    private async Task<bool> PrepareRenewalFulfillmentAsync(Order order, CancellationToken ct)
    {
        var item = order.Items.Single();
        if (item.RenewedFromMembershipId is not { } sourceId || item.ActivationStartDate is not { } start)
            return false;

        // An on-time transfer may arrive after the order expires and the member starts another checkout.
        var renewals = await repository.LoadRenewalOrdersAsync([sourceId], forUpdate: true, ct);
        if (renewals.Any(renewal => renewal.Id != order.Id && renewal.OrderStatus == OrderStatus.Paid))
            return false;

        var current = await repository.LoadCurrentMembershipsAsync(
            [item.BeneficiaryUserId], LocalDate(clock.GetUtcNow()), ct);
        if (HasRenewalOverlap(start, ReadComponentTerms(item), current))
            return false;

        foreach (var replacement in renewals.Where(renewal => renewal.Id != order.Id &&
                     renewal.OrderStatus == OrderStatus.Pending && renewal.PaymentTransaction?.PaymentStatus == PaymentStatus.Pending))
        {
            replacement.OrderStatus = OrderStatus.Cancelled;
            replacement.PaymentTransaction!.PaymentStatus = PaymentStatus.Cancelled;
        }

        return true;
    }

    private static bool HasRenewalOverlap(DateOnly start, IEnumerable<ComponentTerms> terms,
        IEnumerable<MemberMembership> current) =>
        terms.Any(component =>
        {
            var end = EndDate(start, component);
            return current.Any(membership => membership.PackageComponent.ComponentType == component.Type &&
                membership.StartDate <= end && membership.EndDate >= start);
        });

    private static bool CanRenew(ApplicationUser? user) => IsActive(user) && HasRole(user!, UserRole.Member);

    private static Result<T> RenewalForbidden<T>() =>
        Result<T>.Failure("Chỉ tài khoản Hội viên đang hoạt động mới có thể gia hạn.", "RENEWAL_FORBIDDEN", 403);
}
