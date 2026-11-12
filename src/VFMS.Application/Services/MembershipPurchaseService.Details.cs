using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

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
            period.Status.ToString())).ToArray();
        return Result<MembershipDetailResponse>.Success(new(
            ToSummary(detail.Membership, today), detail.PurchasedAt is { } purchasedAt ? Utc(purchasedAt) : null,
            detail.InvoiceNumber, new(detail.TotalFrozenDays, detail.FreezeCount, history)));
    }

    internal static MembershipSummaryResponse ToSummary(OwnedMembership membership, DateOnly today) =>
        new(membership.MembershipId, membership.MembershipPlanId, membership.PlanName,
            membership.ComponentType.ToString(), membership.StartDate, membership.EndDate,
            GetCurrentMembershipStatus(membership.Status, membership.EndDate, today).ToString());

    internal static MembershipStatus GetCurrentMembershipStatus(MembershipStatus status, DateOnly endDate, DateOnly today) =>
        status == MembershipStatus.Active && endDate < today ? MembershipStatus.Expired : status;

    private static Result<T> MembershipNotFound<T>() =>
        Result<T>.Failure("Không tìm thấy gói hội viên.", "MEMBERSHIP_NOT_FOUND", 404);
}
