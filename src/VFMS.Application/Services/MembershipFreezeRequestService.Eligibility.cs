using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipFreezeRequestService
{
    public async Task<Result<MembershipFreezeEligibilityResponse>> GetEligibilityAsync(
        string memberId, int membershipId, int? excludeRequestId, CancellationToken ct)
    {
        var membership = await repository.GetOwnedMembershipAsync(memberId, membershipId, false, ct);
        if (membership is null)
            return NotFound<MembershipFreezeEligibilityResponse>();
        if (excludeRequestId is { } requestId && !membership.MembershipFreezes.Any(
                request => request.Id == requestId && request.RequestedByUserId == memberId &&
                    request.RequestStatus == FreezeRequestStatus.Pending))
            return NotFound<MembershipFreezeEligibilityResponse>();

        var policy = await policies.GetActiveGlobalAsync(false, ct);
        return Result<MembershipFreezeEligibilityResponse>.Success(Evaluate(membership, policy, LocalToday()));
    }

    private static MembershipFreezeEligibilityResponse Evaluate(MemberMembership membership, FreezePolicy? policy, DateOnly today)
    {
        var summary = MembershipPurchaseService.ToSummary(new OwnedMembership(
            membership.Id, membership.OrderItem.PackageId, membership.OrderItem.PackageNameSnapshot,
            membership.PackageComponent.ComponentType, membership.StartDate, membership.EndDate, membership.MembershipStatus), today);
        var paidDays = Math.Max(0, today.DayNumber - membership.StartDate.DayNumber);
        var (days, months) = MembershipPurchaseService.PurchasedDuration(membership.OrderItem.ComponentTermsSnapshot,
            membership.PackageComponentId, membership.PackageComponent.DurationDays, membership.PackageComponent.DurationMonths);
        var unit = days is > 0 ? FreezeTierUnit.Days : FreezeTierUnit.Months;
        var duration = days is > 0 ? days : months;
        var tier = policy?.TierQuotas.SingleOrDefault(quota => quota.Unit == unit && quota.Duration == duration);
        int? remaining = tier is null ? null : (int)Math.Max(0L, tier.MaximumCumulativeFreezeDays - ConsumedDays(membership));
        DateOnly? earliest = policy is { MinimumRequestDays: >= 0 } &&
            (long)today.DayNumber + policy.MinimumRequestDays <= DateOnly.MaxValue.DayNumber
                ? today.AddDays(policy.MinimumRequestDays) : null;
        var configuration = policy?.MinFreezeDaysPerRequest is { } minimum
            ? new MembershipFreezePolicyConfiguration(policy.Id, minimum, policy.DefaultMaxFreezeDays,
                policy.MinimumRequestDays, policy.EligibilityPaidDays, DateTime.SpecifyKind(policy.UpdatedAt, DateTimeKind.Utc))
            : null;

        var response = new MembershipFreezeEligibilityResponse(summary, false, null, null,
            today, earliest, paidDays, remaining, configuration);
        if (summary.Status != nameof(MembershipStatus.Active) || membership.StartDate > today)
            return Blocked(response, "FREEZE_MEMBERSHIP_INACTIVE", "Chỉ gói hội viên đang hoạt động mới có thể gửi yêu cầu bảo lưu.");
        if (membership.PackageComponent.ComponentType == ComponentType.PT)
            return Blocked(response, "FREEZE_PT_PACKAGE", "Gói huấn luyện cá nhân sử dụng quy trình bảo lưu PT.");
        if (membership.PackageComponent.ComponentType == ComponentType.YOGA && days == 7)
            return Blocked(response, "FREEZE_WEEKLY_YOGA", "Gói Yoga theo tuần không hỗ trợ bảo lưu.");
        if (policy is not { IsGlobal: true, PolicyStatus: PolicyStatus.Active, MinFreezeDaysPerRequest: > 0,
                MinimumRequestDays: >= 0, EligibilityPaidDays: >= 0 } ||
            policy.DefaultMaxFreezeDays < policy.MinFreezeDaysPerRequest)
            return Blocked(response, "FREEZE_POLICY_UNAVAILABLE", "Chính sách bảo lưu chưa được cấu hình đầy đủ.");
        if (paidDays < policy.EligibilityPaidDays)
            return Blocked(response, "FREEZE_ELIGIBILITY", $"Gói cần đủ {policy.EligibilityPaidDays} ngày kể từ ngày kích hoạt để gửi yêu cầu.");
        if (duration is not > 0 || tier is null || tier.MaximumCumulativeFreezeDays < 0)
            return Blocked(response, "FREEZE_TIER_UNAVAILABLE", "Chưa có hạn mức bảo lưu cho kỳ hạn gói đã mua.");
        if (remaining < policy.MinFreezeDaysPerRequest)
            return Blocked(response, "MSG-27", "Hạn mức còn lại không đủ cho thời gian bảo lưu tối thiểu.");
        if (earliest is null)
            return Blocked(response, "FREEZE_NOTICE", "Thời gian báo trước vượt quá phạm vi ngày được hỗ trợ.");
        return response with { Eligible = true };
    }

    internal static long ConsumedDays(MemberMembership membership)
    {
        var recorded = membership.MembershipFreezes
            .Where(request => request.RequestStatus is FreezeRequestStatus.Approved or FreezeRequestStatus.Active or FreezeRequestStatus.Completed)
            .Sum(request => Math.Max(0L, (long)(request.ActualEndDate ?? request.RequestedEndDate).DayNumber -
                (request.ActualStartDate ?? request.RequestedStartDate).DayNumber + 1));
        // Older memberships can carry cumulative usage without detailed history. Never add both representations.
        return Math.Max(membership.TotalFrozenDays, recorded);
    }

    private static MembershipFreezeEligibilityResponse Blocked(
        MembershipFreezeEligibilityResponse response, string code, string message) =>
        response with { MessageCode = code, Message = message };

    private static Result<T> Ineligible<T>(MembershipFreezeEligibilityResponse eligibility) =>
        Result<T>.Failure(eligibility.Message!, eligibility.MessageCode!, eligibility.MessageCode switch
        {
            "FREEZE_POLICY_UNAVAILABLE" => 503,
            "FREEZE_MEMBERSHIP_INACTIVE" or "FREEZE_PT_PACKAGE" or "FREEZE_WEEKLY_YOGA" => 409,
            _ => 400
        });

    private static Result<DateOnly> ValidatePeriod(
        MemberMembership membership, MembershipFreezeRequest request, MembershipFreezeEligibilityResponse eligibility, int? excludeId)
    {
        var days = request.RequestedDays!.Value;
        var start = request.StartDate!.Value;
        if (days < eligibility.Policy!.MinimumFreezeDays || days > eligibility.Policy.MaximumFreezeDays ||
            days > eligibility.RemainingQuota)
            return Result<DateOnly>.Failure(
                $"Thời gian bảo lưu phải từ {eligibility.Policy.MinimumFreezeDays} đến {eligibility.Policy.MaximumFreezeDays} ngày. " +
                $"Hạn mức còn lại: {eligibility.RemainingQuota} ngày.", "MSG-27");
        if (start < eligibility.EarliestStartDate)
            return Result<DateOnly>.Failure("Ngày bắt đầu chưa đáp ứng thời gian báo trước.", "FREEZE_NOTICE");
        if ((long)start.DayNumber + days - 1 > DateOnly.MaxValue.DayNumber)
            return Result<DateOnly>.Failure("Thời gian bảo lưu vượt quá phạm vi ngày được hỗ trợ.", "FREEZE_DATE_RANGE");

        var end = start.AddDays(days - 1);
        if (membership.MembershipFreezes.Any(existing => existing.Id != excludeId &&
                existing.RequestStatus is FreezeRequestStatus.Pending or FreezeRequestStatus.Approved or FreezeRequestStatus.Active &&
                (existing.ActualStartDate ?? existing.RequestedStartDate) <= end &&
                (existing.ActualEndDate ?? existing.RequestedEndDate) >= start))
            return Result<DateOnly>.Failure("Thời gian bảo lưu trùng với một yêu cầu đang chờ hoặc đã được duyệt.", "FREEZE_OVERLAP", 409);
        return Result<DateOnly>.Success(end);
    }
}
