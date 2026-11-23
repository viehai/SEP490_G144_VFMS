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
                SetMonthlyTerm(rental, today, Months(Type(rental)));
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
        if (policy is null || Months(policy.RentalType) == 0 || !ValidPolicy(policy))
            return Invalid<LockerCheckout>("Chọn một gói thuê theo tháng.");
        var overdue = OverdueFees(rental, now);
        var charge = BuildCharge(rental, LockerPaymentPurpose.Renewal, method.Value,
            role == UserRole.Member ? OrderChannel.Online : OrderChannel.Counter, policy.RentalPrice + overdue, now);
        charge.RenewalMonths = Months(policy.RentalType);
        charge.RentalExpiryAtQuote = RentalExpiry(rental);
        charge.OverdueFees = overdue;
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        repo.Add(charge);
        await repo.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now, qr), 201);
    }, ct);

    private static bool Money(decimal value, bool zeroAllowed = false) =>
        value >= (zeroAllowed ? 0 : 1) && value <= 1_000_000_000_000m && decimal.Truncate(value) == value;

    // GBR-19: every rental type carries the Key Deposit.
    private static bool ValidPolicy(LockerPricingPolicy policy) =>
        Money(policy.RentalPrice) && Money(policy.DepositAmount) && Money(policy.OverdueDailyFee, true) &&
        (policy.RentalType == RentalType.Daily || Months(policy.RentalType) > 0);

    public Task<Result<IReadOnlyList<LockerPolicy>>> SavePricing(string actor, LockerPricingRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await Actor(actor, UserRole.Manager, ct) is null)
                return Forbidden<IReadOnlyList<LockerPolicy>>();
            var tiers = new (RentalType Type, string Name, decimal Price)[]
            {
                (RentalType.Daily, "Thuê ngày", input.DailyFee),
                (RentalType.Monthly, "1 tháng", input.OneMonth),
                (RentalType.Quarterly, "3 tháng", input.ThreeMonths),
                (RentalType.HalfYearly, "6 tháng", input.SixMonths),
                (RentalType.Yearly, "12 tháng", input.TwelveMonths)
            };
            // UC 5.7 Step 2, MSG-07: every price is a whole VND amount greater than 0.
            if (tiers.Any(p => !Money(p.Price)) || !Money(input.KeyDeposit) || !Money(input.OverdueDailyFee))
                return Invalid<IReadOnlyList<LockerPolicy>>("Vui lòng nhập số tiền VND nguyên lớn hơn 0 cho mọi mức giá, cọc chìa và phí quá hạn.");

            var current = await repo.GetActivePoliciesAsync(ct);
            foreach (var previous in current)
                previous.PolicyStatus = PolicyStatus.Inactive;
            var next = tiers.Select(t => new LockerPricingPolicy
            {
                RentalType = t.Type,
                PolicyName = t.Name,
                RentalPrice = t.Price,
                DepositAmount = input.KeyDeposit,
                OverdueDailyFee = t.Type == RentalType.Daily ? 0 : input.OverdueDailyFee
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
            return Invalid<IReadOnlyList<LockerMember>>("Nhập tên hoặc số điện thoại để tìm hội viên.");
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
