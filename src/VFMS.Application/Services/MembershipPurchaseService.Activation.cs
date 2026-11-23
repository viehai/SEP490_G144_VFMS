using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService
{
    public async Task<Result<MembershipDetailResponse>> ScheduleActivationAsync(
        string memberId, int membershipId, ScheduleMembershipActivationRequest request, CancellationToken ct)
    {
        var result = await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var user = (await repository.LockUsersAsync([memberId], ct)).SingleOrDefault();
            if (!CanPurchase(user))
                return Result<bool>.Failure("Tài khoản không thể kích hoạt gói tập.", "MEMBERSHIP_ACTIVATION_FORBIDDEN", 403);

            var memberships = await repository.LoadOwnedOrderMembershipsAsync(memberId, membershipId, ct);
            var membership = memberships.SingleOrDefault(row => row.Id == membershipId);
            if (membership is null)
                return MembershipNotFound<bool>();

            var item = membership.OrderItem;
            if (item.Order.OrderStatus != OrderStatus.Paid || PurchasedAt(item.Order) is not { } purchasedAt)
                return Result<bool>.Failure("Đơn hàng chưa được thanh toán.", "MEMBERSHIP_ORDER_NOT_PAID", 409);
            if (request.StartDate is not { } start)
                return Result<bool>.Failure("Chọn ngày kích hoạt gói tập.", MembershipPurchaseMessages.ActivationDateCode);
            if (membership.MembershipStatus != MembershipStatus.AwaitingActivation)
                return membership.StartDate == start &&
                    membership.MembershipStatus is MembershipStatus.Active or MembershipStatus.Scheduled
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure("Gói tập đã có lịch kích hoạt hoặc không còn khả dụng.", "MEMBERSHIP_ACTIVATION_ALREADY_SET", 409);

            var today = LocalDate(clock.GetUtcNow());
            var deadline = ActivationDeadline(purchasedAt);
            if (start < today || start < LocalDate(Utc(purchasedAt)) || start > deadline)
                return Result<bool>.Failure($"Chọn ngày từ hôm nay đến {deadline:dd/MM/yyyy}.",
                    MembershipPurchaseMessages.ActivationDateCode);

            var terms = ReadComponentTerms(item).ToDictionary(component => component.Id);
            var awaiting = memberships.Where(row => row.MembershipStatus == MembershipStatus.AwaitingActivation).ToArray();
            if (awaiting.Any(row => !TermFits(terms[row.PackageComponentId], start)))
                return Result<bool>.Failure("Thời hạn gói tập vượt quá phạm vi ngày hỗ trợ.", "MEMBERSHIP_ACTIVATION_TERM_INVALID");

            foreach (var row in awaiting)
                SetMembershipTerm(row, terms[row.PackageComponentId], start, today);

            if (start == today)
                await PromoteAsync([user!], ct);
            await repository.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

        return result.IsSuccess ? await GetOwnedMembershipAsync(memberId, membershipId, ct)
            : Result<MembershipDetailResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
    }

    public async Task AdvanceMembershipsAsync(CancellationToken ct)
    {
        var today = LocalDate(clock.GetUtcNow());
        var cutoff = today.AddDays(1 - MembershipPurchaseMessages.ActivationWindowDays)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - VietnamOffset;
        var userIds = await repository.FindMembershipUsersDueAsync(today, cutoff, ct);
        if (userIds.Length == 0)
            return;

        await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            // Manual selection, payment and the worker serialize entitlement writes on the beneficiary.
            var users = await repository.LockUsersAsync(userIds, ct);
            var due = await repository.LoadDueMembershipsAsync(userIds, today, cutoff, ct);
            var awaiting = due.Where(row => row.MembershipStatus == MembershipStatus.AwaitingActivation).ToArray();
            if (awaiting.Length > 0)
                await AssignOverdueActivationDatesAsync(awaiting, today, ct);

            foreach (var row in due.Where(row => row.StartDate <= today))
                row.MembershipStatus = row.EndDate < today ? MembershipStatus.Expired : MembershipStatus.Active;

            var activeUsers = due.Where(row => row.MembershipStatus == MembershipStatus.Active)
                .Select(row => row.MemberUserId).ToHashSet();
            await PromoteAsync(users.Where(user => activeUsers.Contains(user.UserId)), ct);
            await repository.SaveChangesAsync(ct);
            return due.Count;
        }, ct);
    }

    private async Task AssignOverdueActivationDatesAsync(MemberMembership[] awaiting, DateOnly today, CancellationToken ct)
    {
        var earliestDeadline = awaiting.Min(row => ActivationDeadline(PurchasedAt(row.OrderItem.Order)!.Value));
        var userIds = awaiting.Select(row => row.MemberUserId).Distinct().ToArray();
        var dated = await repository.LoadCurrentMembershipsAsync(userIds, earliestDeadline, ct);
        var byService = dated.ToLookup(row => (row.MemberUserId, row.PackageComponent.ComponentType));
        var queuedEnds = new Dictionary<(string, ComponentType), DateOnly>();
        foreach (var row in awaiting.OrderBy(row => PurchasedAt(row.OrderItem.Order)).ThenBy(row => row.Id))
        {
            var component = ReadComponentTerms(row.OrderItem).Single(terms => terms.Id == row.PackageComponentId);
            var key = (row.MemberUserId, component.Type);
            var deadline = ActivationDeadline(PurchasedAt(row.OrderItem.Order)!.Value);
            // Only the automatic fallback joins an existing queue of dated terms for the same service.
            var end = byService[key].Select(other => other.EndDate).DefaultIfEmpty().Max();
            if (queuedEnds.TryGetValue(key, out var queuedEnd) && queuedEnd > end)
                end = queuedEnd;
            var start = end >= deadline && end < DateOnly.MaxValue ? end.AddDays(1) : deadline;
            if (!TermFits(component, start)) start = deadline;
            SetMembershipTerm(row, component, start, today);
            queuedEnds[key] = row.EndDate;
        }
    }

    private static DateOnly ActivationDeadline(DateTime purchasedAt) =>
        LocalDate(Utc(purchasedAt)).AddDays(MembershipPurchaseMessages.ActivationWindowDays);

    private static DateTime? PurchasedAt(Order order) => order.PaymentTransaction?.PaidAt ?? order.CompletedAt;

    private static bool TermFits(ComponentTerms component, DateOnly start) =>
        TermFits(component.Days ?? component.DurationDays, component.Months ?? component.DurationMonths, start);

    private static void SetMembershipTerm(MemberMembership membership, ComponentTerms component, DateOnly start, DateOnly today)
    {
        membership.StartDate = start;
        membership.EndDate = EndDate(start, component);
        membership.MembershipStatus = membership.EndDate < today ? MembershipStatus.Expired
            : start > today ? MembershipStatus.Scheduled : MembershipStatus.Active;
    }
}
