using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;
using MembershipDurationUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService
{
    public async Task<Result<IReadOnlyList<MembershipSummaryResponse>>> ListOwnedMembershipsAsync(
        string memberId, CancellationToken ct)
    {
        var memberships = await repository.ListOwnedMembershipsAsync(memberId, ct);
        var today = LocalDate(clock.GetUtcNow());
        return Result<IReadOnlyList<MembershipSummaryResponse>>.Success(
            memberships.Select(membership => ToSummary(membership, today)).ToArray());
    }

    public async Task<Result<MembershipDetailResponse>> GetOwnedMembershipAsync(
        string memberId, int membershipId, CancellationToken ct)
    {
        var detail = await repository.FindOwnedMembershipAsync(memberId, membershipId, ct);
        if (detail is null)
            return MembershipNotFound<MembershipDetailResponse>();

        var today = LocalDate(clock.GetUtcNow());
        var history = detail.FreezeHistory.Select(period => new MembershipFreezePeriodResponse(
            period.RequestedStartDate, period.RequestedEndDate, period.ActualStartDate, period.ActualEndDate,
            period.Status.ToString(), (int)MembershipFreezeRequestService.PeriodDays(period.RequestedStartDate, period.RequestedEndDate),
            period.SubmittedAt is { } timestamp ? Utc(timestamp) : null, period.ReviewNote)).ToArray();
        var usage = MembershipFreezeRequestService.AccountedDays(detail.TotalFrozenDays, detail.FreezeHistory.Select(period =>
            (period.ActualStartDate ?? period.RequestedStartDate, period.ActualEndDate ?? period.RequestedEndDate, period.Status)));
        var isYoga = detail.Membership.ComponentType == ComponentType.YOGA;
        var allowance = isYoga ? 0 : detail.GrantedFreezeAllowanceDays;
        return Result<MembershipDetailResponse>.Success(new(
            ToSummary(detail.Membership, today), detail.PurchasedAt is { } purchasedAt ? Utc(purchasedAt) : null,
            detail.InvoiceNumber, new(detail.TotalFrozenDays, detail.FreezeCount, history, allowance,
                usage.Completed, (int)Math.Min(usage.Committed, int.MaxValue),
                MembershipFreezeRequestService.RemainingDays(allowance, usage.Committed),
                isYoga ? null : detail.GrantedFreezeCount, isYoga ? null : detail.GrantedMinFreezeDays, isYoga ? null : detail.GrantedMaxFreezeDays)));
    }

    internal static MembershipSummaryResponse ToSummary(OwnedMembership membership, DateOnly today)
    {
        var awaiting = membership.Status == MembershipStatus.AwaitingActivation;
        MembershipActivationWindow? activation = null;
        if (awaiting && membership.PurchasedAt is { } purchasedAt)
        {
            var purchased = LocalDate(Utc(purchasedAt));
            activation = new(purchased, today > purchased ? today : purchased, ActivationDeadline(purchasedAt));
        }
        var (days, months) = PurchasedDuration(membership.ComponentTermsSnapshot,
            membership.PackageComponentId, currentDays: null, currentMonths: null);
        return new(membership.MembershipId, membership.MembershipPlanId, membership.PlanName,
            membership.ComponentType.ToString(), awaiting ? null : membership.StartDate, awaiting ? null : membership.EndDate,
            GetCurrentMembershipStatus(membership.Status, membership.EndDate, today).ToString(), activation)
        {
            DurationValue = days is > 0 ? days : months is > 0 ? months : null,
            DurationUnit = days is > 0 ? MembershipDurationUnit.Day : months is > 0 ? MembershipDurationUnit.Month : null,
            YogaDifficultyLevel = membership.YogaDifficultyLevel
        };
    }

    internal static MembershipStatus GetCurrentMembershipStatus(MembershipStatus status, DateOnly endDate, DateOnly today) =>
        status == MembershipStatus.Active && endDate < today ? MembershipStatus.Expired : status;

    private static Result<T> MembershipNotFound<T>() =>
        Result<T>.Failure("Không tìm thấy gói hội viên.", "MEMBERSHIP_NOT_FOUND", 404);
}
