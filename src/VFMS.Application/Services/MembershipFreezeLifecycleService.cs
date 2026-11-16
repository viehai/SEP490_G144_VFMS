using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipFreezeLifecycleService(
    IMembershipFreezeLifecycleRepository repository, IMembershipFreezeRequestRepository requests,
    IMembershipFreezePolicyRepository transactions, TimeProvider clock) : IMembershipFreezeLifecycleService
{
    public async Task<Result<bool>> ProcessAsync(int requestId, CancellationToken ct)
    {
        var target = await repository.FindAsync(requestId, ct);
        if (target is null)
            return Result<bool>.Success(false);
        var result = Result<bool>.Success(false);
        await transactions.ExecuteInSerializableTransactionAsync(async () =>
        {
            var membership = await requests.GetOwnedMembershipAsync(target.MemberId, target.MembershipId, true, ct);
            var freeze = membership?.MembershipFreezes.SingleOrDefault(item => item.Id == requestId);
            if (membership is null || freeze is null)
                return;
            if (freeze.RequestedByUserId != membership.MemberUserId)
            {
                result = ReconciliationRequired();
                return;
            }
            var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
            string? message = null;
            if (freeze.RequestStatus == FreezeRequestStatus.Pending && freeze.RequestedStartDate < today)
            {
                freeze.RequestStatus = FreezeRequestStatus.Expired;
                message = "Yêu cầu bảo lưu đã hết hạn do chưa được duyệt trước ngày bắt đầu.";
            }
            else if ((freeze.RequestStatus == FreezeRequestStatus.Approved && freeze.RequestedStartDate <= today) ||
                (freeze.RequestStatus == FreezeRequestStatus.Active && freeze.RequestedEndDate < today))
            {
                if (!HasApprovalEvidence(freeze) ||
                    (freeze.RequestStatus == FreezeRequestStatus.Active && freeze.ActualStartDate is null))
                {
                    result = ReconciliationRequired();
                    return;
                }
                var otherEffective = membership.MembershipFreezes.Any(other => other.Id != freeze.Id &&
                    other.RequestStatus is FreezeRequestStatus.Active or FreezeRequestStatus.Approved &&
                    (other.ActualStartDate ?? other.RequestedStartDate) <= today &&
                    (other.ActualEndDate ?? other.RequestedEndDate) >= today);
                if (freeze.RequestedEndDate >= today &&
                    freeze.RequestStatus == FreezeRequestStatus.Approved && freeze.ActualStartDate is null &&
                    (membership.MembershipStatus is not (MembershipStatus.Active or MembershipStatus.Frozen) || otherEffective))
                {
                    result = ReconciliationRequired();
                    return;
                }
                if (freeze.RequestedEndDate < today)
                {
                    freeze.ActualStartDate ??= freeze.RequestedStartDate;
                    freeze.ActualEndDate = freeze.RequestedEndDate;
                    freeze.RequestStatus = FreezeRequestStatus.Completed;
                    if (freeze.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending)
                        freeze.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Expired;
                    if (!otherEffective && membership.MembershipStatus is MembershipStatus.Frozen or MembershipStatus.Active)
                        membership.MembershipStatus = membership.EndDate < today ? MembershipStatus.Expired : MembershipStatus.Active;
                    message = "Kỳ bảo lưu đã hoàn tất.";
                }
                else if (!otherEffective &&
                    membership.MembershipStatus is MembershipStatus.Active or MembershipStatus.Frozen &&
                    membership.StartDate <= today && membership.EndDate >= today)
                {
                    freeze.ActualStartDate ??= freeze.RequestedStartDate;
                    freeze.RequestStatus = FreezeRequestStatus.Active;
                    membership.MembershipStatus = MembershipStatus.Frozen;
                    message = "Kỳ bảo lưu đã có hiệu lực. Quyền sử dụng gói được tạm dừng trong thời gian bảo lưu.";
                }
                else
                {
                    result = ReconciliationRequired();
                    return;
                }
            }
            if (message is null)
                return;
            var notification = AuditedWriter.Notify(membership.MemberUserId, null, "Bảo lưu hội viên",
                message, nameof(MembershipFreeze), NotificationType.FreezeApproval);
            notification.EntityId = freeze.Id;
            notification.NotifiedAt = clock.GetUtcNow().UtcDateTime;
            repository.Add(notification);
            // UC-17 already charged quota and applied expiry. JOB-03 only changes lifecycle state and history.
            await repository.SaveChangesAsync(ct);
            result = Result<bool>.Success(true);
        }, ct);
        return result;
    }

    private static bool HasApprovalEvidence(MembershipFreeze freeze)
    {
        var days = freeze.RequestedEndDate.DayNumber - freeze.RequestedStartDate.DayNumber + 1;
        var snapshot = MembershipFreezeRequestService.ReadAppliedPolicy(freeze.AppliedPolicySnapshot);
        return days > 0 && freeze.AppliedExpiryExtensionDays == days && freeze.EarlyUnfreezeAt is null &&
            snapshot?.Policy is { MinimumFreezeDays: > 0 } && snapshot.Quotas is { Count: > 0 } &&
            (freeze.ActualStartDate is null || freeze.ActualStartDate == freeze.RequestedStartDate) &&
            (freeze.ActualEndDate is null || freeze.ActualEndDate == freeze.RequestedEndDate);
    }

    private static Result<bool> ReconciliationRequired() => Result<bool>.Failure(
        "Bản ghi bảo lưu cần đối soát lịch sử trước khi chuyển trạng thái.", "FREEZE_RECONCILIATION_REQUIRED", 409);
}
