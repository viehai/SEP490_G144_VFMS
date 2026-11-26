using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService
{
    public Task<Result<LockerRentalView>> Handover(string actor, int id, LockerHandoverRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerRentalView>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var current = Now;
            var now = current.UtcDateTime;
            if (rental.HandedOverAt is not null)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (!request.TermsAccepted || !request.IdentityVerified)
                return Invalid<LockerRentalView>("Cần xác minh hội viên, xác nhận đồng ý điều khoản và giao chìa.");
            var payment = rental.Payments.SingleOrDefault(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
            var collectCash = request.CashAmount is not null && rental.ContractStatus == LockerContractStatus.PendingPayment;
            if (payment is null || rental.CollectBy <= now || rental.Locker.LockerStatus != LockerStatus.Reserved)
                return Conflict<LockerRentalView>("Hợp đồng không còn hiệu lực hoặc đã quá hạn nhận chìa.");
            if (!await Eligible(rental.CustomerUserId, now, ct))
                return MembershipRequired<LockerRentalView>();

            if (collectCash)
            {
                if (payment.Order.Channel != OrderChannel.Counter || payment.PaymentMethod != PaymentMethod.Cash ||
                    request.CashAmount != payment.AmountDue || !Pending(payment, now) ||
                    !await CanFulfill(payment, now, now, ct))
                    return Conflict<LockerRentalView>("Kiểm tra phương thức, số tiền và thời hạn của đơn tại quầy.");
            }
            else if (rental.ContractStatus != LockerContractStatus.PendingHandover ||
                payment.PaymentStatus != PaymentStatus.Paid || payment.PaidAt is null)
                return Conflict<LockerRentalView>("Đơn chưa thanh toán.");

            var today = LocalDate(now);
            var paidAt = collectCash ? now : payment.PaidAt!.Value;
            if (Type(rental) == RentalType.Daily &&
                (today != LocalDate(paidAt) || !OpenForDailyRental(rental.Locker.LockerZone.Gym, now)))
                return Conflict<LockerRentalView>("Chỉ giao chìa thuê ngày trong giờ mở cửa của ngày đã thanh toán.");
            if (collectCash)
                await ApplyPayment(payment, payment.AmountDue, current, null, current, actor, ct);
            if (Type(rental) == RentalType.Daily)
            {
                var gym = rental.Locker.LockerZone.Gym;
                rental.StartDate = today;
                rental.EndDate = today;
                rental.ExpiresAt = today.ToDateTime(gym.CloseTime!.Value, DateTimeKind.Utc).Subtract(LocalOffset);
            }
            else
            {
                SetMonthlyTerm(rental, today, rental.TermMonthsSnapshot ?? Months(rental.LockerPricingPolicy));
            }
            rental.CollectBy = null;
            rental.HandedOverAt = now;
            rental.HandedOverByUserId = actor;
            rental.ContractStatus = LockerContractStatus.Active;
            rental.Locker.LockerStatus = LockerStatus.Rented;
            Audit(actor, rental, "LockerKeyHandedOver:IdentityVerified:TermsAccepted", now);
            await repo.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public Task<Result<LockerCheckout>> Renew(string actor, UserRole role, int id, LockerRenewalRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        var method = role == UserRole.Member ? PaymentMethod.VietQR : ParseCollection(request.PaymentMethod);
        if (method is null)
            return Invalid<LockerCheckout>("Chọn Cash hoặc VietQR.");
        var rental = await repo.GetRentalAsync(id, user.GymId, role == UserRole.Member ? actor : null, ct);
        if (rental is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        if (!InUse(rental) || rental.ReturnedAt is not null ||
            OverdueDays(rental, now) > 7 || Type(rental) == RentalType.Daily ||
            rental.Locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged)
            return Conflict<LockerCheckout>("Hợp đồng không đủ điều kiện gia hạn.");
        if (!await Eligible(rental.CustomerUserId, now, ct))
            return MembershipRequired<LockerCheckout>();
        if (rental.Payments.Any(p => Pending(p, now) || IsPaidSettlement(p)))
            return Conflict<LockerCheckout>("Hợp đồng đang có khoản thu gia hạn hoặc tất toán cần xử lý.");
        var policy = await repo.GetActivePolicyAsync(request.PolicyId, ct);
        if (policy is null || Months(policy) == 0 || !ValidPolicy(policy))
            return Invalid<LockerCheckout>("Chọn một gói thuê theo tháng.");
        var overdue = OverdueFees(rental, now);
        var charge = BuildCharge(rental, LockerPaymentPurpose.Renewal, method.Value,
            role == UserRole.Member ? OrderChannel.Online : OrderChannel.Counter, policy.RentalPrice + overdue, now);
        charge.RenewalMonths = Months(policy);
        charge.PaymentStartedAt = request.ChooseAtCheckout ? null : now;
        if (request.ChooseAtCheckout)
        {
            charge.HoldExpiresAt = now.Add(HoldWindow);
            charge.GatewayName = PendingGateway;
        }
        charge.RentalExpiryAtQuote = RentalExpiry(rental);
        charge.OverdueFees = overdue;
        var qr = ChoosingPayment(charge) ? null : Qr(charge);
        if (!ChoosingPayment(charge) && method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        repo.Add(charge);
        await repo.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now, qr), 201);
    }, ct);

    private static bool Money(decimal value, bool zeroAllowed = false) =>
        value >= (zeroAllowed ? 0 : 1) && value <= 1_000_000_000_000m && decimal.Truncate(value) == value;

    // Daily rentals collect only the key deposit; monthly rentals also collect their rental fee.
    private static bool ValidPolicy(LockerPricingPolicy policy) =>
        Money(policy.RentalPrice, policy.RentalType == RentalType.Daily) &&
        Money(policy.DepositAmount) && Money(policy.OverdueDailyFee, true) &&
        (policy.RentalType == RentalType.Daily || Months(policy) is >= 1 and <= 1200);

    public Task<Result<IReadOnlyList<LockerPolicy>>> SavePricing(string actor, LockerPricingRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await Actor(actor, UserRole.Manager, ct) is null)
                return Forbidden<IReadOnlyList<LockerPolicy>>();
            var tiers = input.Tiers ?? [new(1, input.OneMonth), new(3, input.ThreeMonths),
                new(6, input.SixMonths), new(12, input.TwelveMonths)];
            // DailyFee remains in the request for compatibility, but new Daily rentals have no rental fee.
            if (!Money(input.DailyFee, true) || tiers.Count == 0 ||
                tiers.Any(p => p is null || p.Months is < 1 or > 1200 || !Money(p.RentalFee)) ||
                tiers.Select(p => p.Months).Distinct().Count() != tiers.Count ||
                !Money(input.KeyDeposit) || !Money(input.OverdueDailyFee))
                return Invalid<IReadOnlyList<LockerPolicy>>("Mỗi kỳ thuê phải có số tháng nguyên từ 1 đến 1.200, không trùng nhau; phí thuê, cọc chìa và phí quá hạn phải là số tiền VND nguyên lớn hơn 0.");

            var current = await repo.GetActivePoliciesAsync(ct);
            foreach (var previous in current)
                previous.PolicyStatus = PolicyStatus.Inactive;
            var next = new[] { new LockerPricingTier(0, 0) }.Concat(tiers.OrderBy(t => t.Months)).Select(t => new LockerPricingPolicy
            {
                RentalType = t.Months switch { 0 => RentalType.Daily, 3 => RentalType.Quarterly,
                    6 => RentalType.HalfYearly, 12 => RentalType.Yearly, _ => RentalType.Monthly },
                DurationMonths = t.Months,
                PolicyName = t.Months == 0 ? "Thuê ngày" : $"{t.Months} tháng",
                RentalPrice = t.RentalFee,
                DepositAmount = input.KeyDeposit,
                OverdueDailyFee = t.Months == 0 ? 0 : input.OverdueDailyFee
            }).ToArray();
            foreach (var policy in next)
                repo.Add(policy);
            repo.Add(new AuditLog
            {
                ActorUserId = actor,
                ActionType = AuditActionType.Update,
                AuditEntity = nameof(LockerPricingPolicy),
                NewValue = FormattableString.Invariant(
                    $"LockerPriceListSaved:Deposit={input.KeyDeposit}:OverdueDaily={input.OverdueDailyFee}"),
                OccurredAt = Now.UtcDateTime
            });
            await repo.SaveChangesAsync(ct);
            return Result<IReadOnlyList<LockerPolicy>>.Success(next.Select(Policy).ToArray());
        }, ct);

    public async Task<Result<IReadOnlyList<LockerMember>>> FindMembers(string actor, string? search, CancellationToken ct)
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<IReadOnlyList<LockerMember>>();
        search = search?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 100)
            return Invalid<IReadOnlyList<LockerMember>>("Nhập tên, số điện thoại hoặc email để tìm hội viên.");
        var now = Now.UtcDateTime;
        var users = await repo.SearchMembersAsync(staff.GymId, search, now, LocalDate(now), ct);
        return Result<IReadOnlyList<LockerMember>>.Success(users);
    }

    public async Task<Result<IReadOnlyList<LockerStaffRental>>> Registry(
        string actor, UserRole role, string? search, bool overdueOnly, CancellationToken ct)
    {
        var staff = await Actor(actor, role, ct);
        if (staff is null)
            return Forbidden<IReadOnlyList<LockerStaffRental>>();
        search = search?.Trim();
        if (search?.Length > 100)
            return Invalid<IReadOnlyList<LockerStaffRental>>("Từ khóa tìm kiếm tối đa 100 ký tự.");
        var now = Now.UtcDateTime;
        var today = LocalDate(now);
        var data = await repo.GetRegistryAsync(staff.GymId, now, today, search, overdueOnly, ct);
        return Result<IReadOnlyList<LockerStaffRental>>.Success(data.Select(r =>
            new LockerStaffRental(View(r, now), r.CustomerUserId, r.Customer.FullName, r.Customer.PhoneNumber)).ToArray());
    }

    public async Task<Result<LockerStaffRental>> Details(string actor, UserRole role, int id, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerStaffRental>();
        var rental = await StaffRental(user, id, ct);
        return rental is null ? Missing<LockerStaffRental>() : Result<LockerStaffRental>.Success(
            new(View(rental, Now.UtcDateTime), rental.CustomerUserId, rental.Customer.FullName, rental.Customer.PhoneNumber));
    }
}
