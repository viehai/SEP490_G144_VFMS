using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipFreezeRequestService
{
    internal const string YogaFreezeUnsupportedCode = "FREEZE_YOGA_PACKAGE";
    internal const string YogaFreezeUnsupportedMessage = "Gói Yoga không hỗ trợ bảo lưu.";

    public async Task<Result<MembershipFreezeEligibilityResponse>> GetEligibilityAsync(
        string memberId, int membershipId, int? excludeRequestId, CancellationToken ct)
    {
        var membership = await repository.GetOwnedMembershipAsync(memberId, membershipId, false, ct);
        if (membership is null)
            return NotFound<MembershipFreezeEligibilityResponse>();
        MembershipFreeze? existing = null;
        if (excludeRequestId is { } requestId)
        {
            existing = membership.MembershipFreezes.SingleOrDefault(request => request.Id == requestId &&
                request.RequestedByUserId == memberId && request.RequestStatus == FreezeRequestStatus.Pending);
            if (existing is null)
                return NotFound<MembershipFreezeEligibilityResponse>();
        }

        var policy = existing is null && membership.PackageComponent.ComponentType != ComponentType.YOGA
            ? await policies.GetActiveGlobalAsync(false, ct) : null;
        return Result<MembershipFreezeEligibilityResponse>.Success(Evaluate(membership,
            MembershipFreezePolicyService.Configuration(policy), LocalToday(), existing));
    }

    private static MembershipFreezeEligibilityResponse Evaluate(MemberMembership membership,
        MembershipFreezePolicyConfiguration? policy, DateOnly today, MembershipFreeze? existing)
    {
        var summary = MembershipPurchaseService.ToSummary(new OwnedMembership(
            membership.Id, membership.OrderItem.PackageId, membership.OrderItem.PackageNameSnapshot,
            membership.PackageComponent.ComponentType, membership.StartDate, membership.EndDate, membership.MembershipStatus), today);
        var paidDays = Math.Max(0, today.DayNumber - membership.StartDate.DayNumber);
        var (committed, completed) = AccountedDays(membership);
        if (membership.PackageComponent.ComponentType == ComponentType.YOGA)
            return new(summary, false, YogaFreezeUnsupportedCode, YogaFreezeUnsupportedMessage, today, null, paidDays, 0, null,
                GrantedFreezeAllowanceDays: 0, CompletedFreezeDays: completed, CommittedFreezeDays: (int)Math.Min(int.MaxValue, committed));

        var snapshot = existing is null ? null : ReadAppliedPolicy(existing.AppliedPolicySnapshot);
        if (existing is not null)
            policy = snapshot?.Policy;
        var visitLimit = membership.GrantedFreezeCount;
        if (visitLimit.HasValue && policy is not null)
            policy = policy with { MinimumFreezeDays = membership.GrantedMinFreezeDays ?? 0,
                MaximumFreezeDays = membership.GrantedMaxFreezeDays ?? 0 };
        var pending = membership.MembershipFreezes.Count(row => row.RequestStatus == FreezeRequestStatus.Pending && row.Id != existing?.Id);
        int? remainingVisits = visitLimit.HasValue ? Math.Max(0, visitLimit.Value - membership.FreezeCount - pending) : null;
        var allowance = ApplicableAllowance(membership, snapshot);
        var remaining = RemainingDays(allowance, committed);
        var earliest = policy is { MinimumRequestDays: >= 0 } ? AddDaysIfSupported(today, policy.MinimumRequestDays) : null;
        var overlaps = membership.MembershipFreezes
            .Where(request => request.Id != existing?.Id && request.RequestStatus is
                FreezeRequestStatus.Pending or FreezeRequestStatus.Approved or FreezeRequestStatus.Active)
            .Select(request => new MembershipFreezeBlockedPeriod(request.Id, request.ActualStartDate ?? request.RequestedStartDate,
                request.ActualEndDate ?? request.RequestedEndDate, request.RequestStatus.ToString()))
            .OrderBy(period => period.StartDate).ToArray();

        var response = new MembershipFreezeEligibilityResponse(summary, false, null, null, today, earliest, paidDays,
            remaining, policy, LatestStartDate: summary.EndDate, OverlappingPeriods: overlaps,
            GrantedFreezeAllowanceDays: membership.GrantedFreezeAllowanceDays,
            CompletedFreezeDays: completed, CommittedFreezeDays: (int)Math.Min(int.MaxValue, committed), RemainingFreezeCount: remainingVisits);
        if (summary.Status == nameof(MembershipStatus.Frozen))
            return Blocked(response, "FREEZE_MEMBERSHIP_INACTIVE", "Gói đang được bảo lưu. Bạn có thể quản lý yêu cầu hiện tại hoặc yêu cầu kết thúc sớm.");
        if (summary.Status != nameof(MembershipStatus.Active) || membership.StartDate > today)
            return Blocked(response, "FREEZE_MEMBERSHIP_INACTIVE", "Chỉ gói hội viên đang hoạt động mới có thể gửi yêu cầu bảo lưu.");
        if (membership.PackageComponent.ComponentType == ComponentType.PT)
            return Blocked(response, "FREEZE_PT_PACKAGE", "Gói huấn luyện cá nhân sử dụng quy trình bảo lưu PT.");
        if (remainingVisits == 0)
            return Blocked(response, "FREEZE_VISITS_EXHAUSTED", "Không còn lượt bảo lưu khả dụng cho gói này.");
        if (policy is not { MinimumFreezeDays: > 0, MinimumRequestDays: >= 0, EligibilityPaidDays: >= 0 } ||
            policy.MaximumFreezeDays < policy.MinimumFreezeDays)
            return Blocked(response, "FREEZE_POLICY_UNAVAILABLE", "Chính sách bảo lưu chưa được cấu hình đầy đủ.");
        if (!visitLimit.HasValue && allowance is null)
            return Blocked(response, "FREEZE_ALLOWANCE_UNVERIFIED", "Chưa xác định được số ngày bảo lưu của gói đã mua. Vui lòng liên hệ quản lý.");
        if (!visitLimit.HasValue && allowance == 0)
            return Blocked(response, "FREEZE_ALLOWANCE_DISABLED", "Gói này không có quyền lợi bảo lưu.");
        if (paidDays < policy.EligibilityPaidDays)
        {
            var eligibleOn = AddDaysIfSupported(membership.StartDate, policy.EligibilityPaidDays);
            return Blocked(response, "FREEZE_ELIGIBILITY", eligibleOn is { } date
                ? $"Bạn có thể gửi yêu cầu bảo lưu từ ngày {date:dd/MM/yyyy}."
                : "Thời gian tối thiểu kể từ ngày kích hoạt vượt quá phạm vi ngày được hỗ trợ.");
        }
        if (remaining == 0)
            return Blocked(response, "MSG-27", "Bạn đã sử dụng hết số ngày được bảo lưu của gói này.");
        if (remaining < policy.MinimumFreezeDays)
            return Blocked(response, "MSG-27", $"Bạn còn {remaining} ngày bảo lưu, nhưng mỗi yêu cầu cần ít nhất {policy.MinimumFreezeDays} ngày.");
        if (earliest is null || earliest > membership.EndDate)
            return Blocked(response, "FREEZE_NOTICE", "Gói sẽ hết hạn trước ngày có thể bắt đầu bảo lưu.");

        var maximum = Math.Min(Math.Min(policy.MaximumFreezeDays, remaining ?? policy.MaximumFreezeDays), DateOnly.MaxValue.DayNumber - membership.EndDate.DayNumber);
        var latestStart = Math.Min(membership.EndDate.DayNumber, DateOnly.MaxValue.DayNumber - policy.MinimumFreezeDays);
        var cursor = earliest.Value.DayNumber;
        var best = 0;
        int? firstAvailable = null, lastAvailable = null;
        foreach (var overlap in overlaps)
        {
            if (cursor > latestStart)
                break;
            if (overlap.EndDate.DayNumber < cursor)
                continue;
            var gap = overlap.StartDate.DayNumber - cursor;
            if (gap >= policy.MinimumFreezeDays)
            {
                firstAvailable ??= cursor;
                lastAvailable = Math.Min(latestStart, overlap.StartDate.DayNumber - policy.MinimumFreezeDays);
            }
            best = Math.Max(best, gap);
            cursor = overlap.EndDate.DayNumber + 1;
        }
        if (cursor <= latestStart)
        {
            firstAvailable ??= cursor;
            lastAvailable = latestStart;
            best = Math.Max(best, DateOnly.MaxValue.DayNumber - cursor);
        }
        var available = Math.Min(maximum, best);
        if (available < policy.MinimumFreezeDays)
            return Blocked(response, overlaps.Length > 0 ? "FREEZE_OVERLAP" : "FREEZE_DATE_RANGE", overlaps.Length > 0
                ? "Không còn khoảng ngày phù hợp. Hãy kiểm tra yêu cầu đang chờ duyệt hoặc đã được duyệt."
                : "Gói không còn khoảng ngày hợp lệ để bảo lưu.");

        return response with { Eligible = true, MaximumAvailableDays = available,
            EarliestStartDate = DateOnly.FromDayNumber(firstAvailable!.Value), LatestStartDate = DateOnly.FromDayNumber(lastAvailable!.Value) };
    }

    internal static int? ApplicableAllowance(MemberMembership membership, MembershipFreezePolicyResponse? snapshot)
    {
        // Legacy requests used the quota in the submitted policy. It applies only to that existing request.
        if (snapshot is { GrantedFreezeAllowanceDays: null, Quotas: { Count: > 0 } quotas })
        {
            // The previous submission service wrote exactly one already-resolved tier.
            if (quotas is [{ MaximumCumulativeFreezeDays: >= 0 } captured])
                return captured.MaximumCumulativeFreezeDays;
            var (days, months) = MembershipPurchaseService.PurchasedDuration(membership.OrderItem.ComponentTermsSnapshot,
                membership.PackageComponentId, membership.PackageComponent.DurationDays, membership.PackageComponent.DurationMonths);
            var matches = quotas.Where(quota => quota is not null && quota.Duration == (days is > 0 ? days : months) &&
                quota.Unit == (days is > 0 ? MembershipFreezeTierUnits.Days : MembershipFreezeTierUnits.Months)).ToArray();
            return matches is [{ MaximumCumulativeFreezeDays: >= 0 } tier] ? tier.MaximumCumulativeFreezeDays : null;
        }
        // Historical snapshots without either a resolved tier or a granted allowance are incomplete.
        return snapshot is null || snapshot.GrantedFreezeAllowanceDays.HasValue ? membership.GrantedFreezeAllowanceDays : null;
    }

    internal static long ConsumedDays(MemberMembership membership) => AccountedDays(membership).Committed;

    private static (long Committed, int Completed) AccountedDays(MemberMembership membership) =>
        AccountedDays(membership.TotalFrozenDays, membership.MembershipFreezes.Select(request =>
            (request.ActualStartDate ?? request.RequestedStartDate, request.ActualEndDate ?? request.RequestedEndDate, request.RequestStatus)));

    internal static (long Committed, int Completed) AccountedDays(int total, IEnumerable<(DateOnly Start, DateOnly End, FreezeRequestStatus Status)> periods)
    {
        long inProgress = 0, completed = 0;
        foreach (var (start, end, status) in periods)
        {
            if (status is FreezeRequestStatus.Approved or FreezeRequestStatus.Active)
                inProgress += PeriodDays(start, end);
            else if (status == FreezeRequestStatus.Completed)
                completed += PeriodDays(start, end);
        }
        // Older memberships can carry cumulative usage without detailed history. Never add both representations.
        var committed = Math.Max(total, inProgress + completed);
        return (committed, (int)Math.Min(int.MaxValue, committed - inProgress));
    }

    internal static long PeriodDays(DateOnly start, DateOnly end) => Math.Max(0L, (long)end.DayNumber - start.DayNumber + 1);

    internal static int? RemainingDays(int? allowance, long committed) =>
        allowance is { } granted ? (int)Math.Max(0L, granted - committed) : null;

    private static DateOnly? AddDaysIfSupported(DateOnly date, int days) =>
        (long)date.DayNumber + days <= DateOnly.MaxValue.DayNumber ? date.AddDays(days) : null;

    private static MembershipFreezeEligibilityResponse Blocked(
        MembershipFreezeEligibilityResponse response, string code, string message) =>
        response with { MessageCode = code, Message = message };

    private static Result<T> Ineligible<T>(MembershipFreezeEligibilityResponse eligibility) =>
        Result<T>.Failure(eligibility.Message!, eligibility.MessageCode!, eligibility.MessageCode switch
        {
            "FREEZE_POLICY_UNAVAILABLE" => 503,
            "FREEZE_MEMBERSHIP_INACTIVE" or "FREEZE_PT_PACKAGE" or YogaFreezeUnsupportedCode or "FREEZE_OVERLAP" => 409,
            _ => 400
        });

    private static Result<DateOnly> ValidatePeriod(MembershipFreezeRequest request, MembershipFreezeEligibilityResponse eligibility)
    {
        var start = request.StartDate!.Value;
        var end = request.EndDate ?? AddDaysIfSupported(start, request.RequestedDays!.Value - 1);
        if (end is null || end == DateOnly.MaxValue)
            return Result<DateOnly>.Failure("Thời gian bảo lưu vượt quá phạm vi ngày được hỗ trợ.", "FREEZE_DATE_RANGE");
        if (start > eligibility.Membership.EndDate)
            return Result<DateOnly>.Failure("Ngày bắt đầu bảo lưu phải nằm trong thời hạn của gói.", "FREEZE_DATE_RANGE");
        var days = PeriodDays(start, end.Value);
        if (eligibility.OverlappingPeriods!.Any(period => period.StartDate <= end && period.EndDate >= start))
            return Result<DateOnly>.Failure("Khoảng ngày trùng với yêu cầu đang chờ duyệt hoặc đã được duyệt.", "FREEZE_OVERLAP", 409);
        if (start < eligibility.EarliestStartDate)
            return Result<DateOnly>.Failure("Ngày bắt đầu chưa đáp ứng thời gian báo trước.", "FREEZE_NOTICE");
        if (days < eligibility.Policy!.MinimumFreezeDays || days > eligibility.MaximumAvailableDays)
            return Result<DateOnly>.Failure(
                $"Bạn có thể bảo lưu từ {eligibility.Policy.MinimumFreezeDays} đến {eligibility.MaximumAvailableDays} ngày trong lần này. ", "MSG-27");
        return Result<DateOnly>.Success(end.Value);
    }
}
