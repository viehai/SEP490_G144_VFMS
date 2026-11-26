using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipAccessService(IMembershipAccessRepository repository, TimeProvider clock) : IMembershipAccessService
{
    public async Task<Result<bool>> CheckGymEntryAsync(string memberId, int membershipId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now + YogaTime.GymOffset);
        var plans = await repository.GetEntryPlansAsync([memberId], today, now, ct);
        var plan = EntryPlan.Best(plans.Where(p => p.MemberMembershipId == membershipId));
        if (plan is null) return NotFound();
        return plan.IsActive ? Result<bool>.Success(true) : Denied("MSG-36");
    }

    public async Task<Result<bool>> CheckClassBookingAsync(string memberId, int membershipId, CancellationToken ct)
    {
        var membership = await repository.FindOwnedAsync(memberId, membershipId, ct);
        if (membership is null) return NotFound();
        var today = YogaTime.Today(clock);
        return membership.ComponentType == ComponentType.YOGA && membership.Status == MembershipStatus.Active &&
            membership.StartDate <= today && membership.EndDate >= today
                ? Result<bool>.Success(true)
                : Denied("MSG-31");
    }

    private static Result<bool> NotFound() =>
        Result<bool>.Failure("Không tìm thấy gói hội viên.", "MEMBERSHIP_NOT_FOUND", 404);

    private static Result<bool> Denied(string code) =>
        Result<bool>.Failure("Gói hội viên không có quyền sử dụng dịch vụ ở trạng thái hiện tại.", code, 403);
}
