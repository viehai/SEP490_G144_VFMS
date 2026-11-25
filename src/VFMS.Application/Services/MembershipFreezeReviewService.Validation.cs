using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipFreezeReviewService
{
    private static Result<int> CheckApproval(MemberMembership membership, MembershipFreeze freeze, bool early, DateOnly today)
    {
        if (!IsPending(freeze, early))
            return InvalidApproval("Yêu cầu không còn chờ duyệt.", "FREEZE_REVIEW_STATE");
        if (freeze.MemberMembershipId != membership.Id || freeze.RequestedByUserId != membership.MemberUserId)
            return InvalidApproval("Yêu cầu không thuộc hội viên của gói.", "FREEZE_REVIEW_STATE");
        var days = Duration(freeze);
        if (days <= 0)
            return InvalidApproval("Thời gian bảo lưu không hợp lệ.", "FREEZE_REVIEW_PERIOD");
        if (early)
        {
            var start = freeze.ActualStartDate ?? freeze.RequestedStartDate;
            var end = freeze.ActualEndDate ?? freeze.RequestedEndDate;
            if (membership.MembershipStatus != MembershipStatus.Frozen ||
                freeze.RequestStatus is not (FreezeRequestStatus.Approved or FreezeRequestStatus.Active) ||
                freeze.EarlyUnfreezeAt is not null || start > today || today >= end)
                return InvalidApproval("Chỉ có thể kết thúc sớm một kỳ bảo lưu đang có hiệu lực trước ngày kết thúc.", "FREEZE_EARLY_STATE");
            if (freeze.AppliedExpiryExtensionDays != days)
                return InvalidApproval("Chưa xác định được số ngày đã gia hạn của kỳ bảo lưu này.", "FREEZE_EXTENSION_UNKNOWN");
            // Both the original start and the effective early-unfreeze date count as frozen calendar days.
            var actual = today.DayNumber - start.DayNumber + 1;
            var unused = days - actual;
            var consumed = MembershipFreezeRequestService.ConsumedDays(membership);
            if (actual <= 0 || unused <= 0 || consumed < unused || consumed - unused > int.MaxValue ||
                membership.EndDate.DayNumber < unused)
                return InvalidApproval("Không thể đối soát thời gian bảo lưu đã áp dụng.", "FREEZE_REVIEW_PERIOD");
            return Result<int>.Success(actual);
        }

        if (membership.PackageComponent.ComponentType == ComponentType.YOGA)
            return InvalidApproval(MembershipFreezeRequestService.YogaFreezeUnsupportedMessage, MembershipFreezeRequestService.YogaFreezeUnsupportedCode);
        if (freeze.AppliedExpiryExtensionDays is not null ||
            MembershipPurchaseService.GetCurrentMembershipStatus(membership.MembershipStatus, membership.EndDate, today) != MembershipStatus.Active ||
            membership.StartDate > today)
            return InvalidApproval("Gói hội viên không còn ở trạng thái cho phép duyệt bảo lưu.", "FREEZE_REVIEW_MEMBERSHIP");
        if (freeze.RequestedStartDate < today)
            return InvalidApproval("Ngày bắt đầu đã qua. Yêu cầu không thể được duyệt hồi tố.", "FREEZE_REVIEW_PERIOD");
        if (freeze.RequestedStartDate > membership.EndDate)
            return InvalidApproval("Ngày bắt đầu bảo lưu nằm ngoài thời hạn hiệu lực hiện tại của gói.", "FREEZE_REVIEW_MEMBERSHIP");
        var snapshot = MembershipFreezeRequestService.ReadAppliedPolicy(freeze.AppliedPolicySnapshot);
        if (snapshot?.Policy is not { MinimumFreezeDays: > 0 } conditions ||
            days < conditions.MinimumFreezeDays || days > conditions.MaximumFreezeDays)
            return InvalidApproval("Thiếu hoặc không hợp lệ quy tắc đã áp dụng khi gửi yêu cầu.", "FREEZE_POLICY_SNAPSHOT");
        var allowance = MembershipFreezeRequestService.ApplicableAllowance(membership, snapshot);
        if (membership.GrantedFreezeCount is { } count && membership.FreezeCount >= count)
            return InvalidApproval("Gói đã dùng hết số lượt bảo lưu.", "FREEZE_VISITS_EXHAUSTED");
        if (membership.GrantedFreezeCount is null && allowance is null)
            return InvalidApproval("Chưa xác định được số ngày được bảo lưu của gói đã mua.", "FREEZE_POLICY_SNAPSHOT");
        var used = MembershipFreezeRequestService.ConsumedDays(membership);
        if (membership.GrantedFreezeCount is null && used + days > allowance)
            return InvalidApproval("Số ngày còn được bảo lưu không đủ để duyệt yêu cầu.", "MSG-27");
        if ((long)membership.EndDate.DayNumber + days > DateOnly.MaxValue.DayNumber || membership.FreezeCount == int.MaxValue)
            return InvalidApproval("Gia hạn vượt quá phạm vi được hỗ trợ.", "FREEZE_REVIEW_PERIOD");
        if (membership.MembershipFreezes.Any(other => other.Id != freeze.Id &&
                other.RequestStatus is FreezeRequestStatus.Approved or FreezeRequestStatus.Active or FreezeRequestStatus.Completed &&
                (other.ActualStartDate ?? other.RequestedStartDate) <= freeze.RequestedEndDate &&
                (other.ActualEndDate ?? other.RequestedEndDate) >= freeze.RequestedStartDate))
            return InvalidApproval("Thời gian yêu cầu trùng với kỳ bảo lưu đã có hiệu lực hoặc đã được duyệt.", "FREEZE_OVERLAP");
        return Result<int>.Success(days);
    }

    private static Result<int> InvalidApproval(string message, string code) => Result<int>.Failure(message, code, 409);
}
