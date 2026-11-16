using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipAccessService(IMembershipAccessRepository repository, TimeProvider clock) : IMembershipAccessService
{
    public Task<Result<bool>> CheckGymEntryAsync(string memberId, int membershipId, CancellationToken ct) =>
        CheckAsync(memberId, membershipId, ComponentType.GYM, "MSG-36", ct);

    public Task<Result<bool>> CheckClassBookingAsync(string memberId, int membershipId, CancellationToken ct) =>
        CheckAsync(memberId, membershipId, ComponentType.YOGA, "MSG-31", ct);

    private async Task<Result<bool>> CheckAsync(
        string memberId, int membershipId, ComponentType component, string code, CancellationToken ct)
    {
        var membership = await repository.FindOwnedAsync(memberId, membershipId, ct);
        if (membership is null)
            return Result<bool>.Failure("Không tìm thấy gói hội viên.", "MEMBERSHIP_NOT_FOUND", 404);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        return membership.ComponentType == component && membership.Status == MembershipStatus.Active &&
            membership.StartDate <= today && membership.EndDate >= today
                ? Result<bool>.Success(true)
                : Result<bool>.Failure("Gói hội viên không có quyền sử dụng dịch vụ ở trạng thái hiện tại.", code, 403);
    }
}
