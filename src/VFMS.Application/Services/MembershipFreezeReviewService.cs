using System.Text.Json;
using FluentValidation;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipFreezeReviewService(
    IMembershipFreezeReviewRepository repository, IMembershipFreezeRequestRepository requests,
    IMembershipFreezePolicyRepository policies, IValidator<RejectMembershipFreezeRequest> validator, TimeProvider clock,
    IMembershipFreezeDocumentStore? documents = null)
    : IMembershipFreezeReviewService
{
    public async Task<Result<PagedResponse<MembershipFreezeReviewSummaryResponse>>> ListAsync(
        MembershipFreezeReviewListRequest request, CancellationToken ct)
    {
        if (request.Type is not (null or "" or MembershipFreezeReviewTypes.Freeze or MembershipFreezeReviewTypes.EarlyUnfreeze) ||
            request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return Result<PagedResponse<MembershipFreezeReviewSummaryResponse>>.Failure("Bộ lọc không hợp lệ.");
        var (items, count) = await repository.SearchAsync(request.Type, request.Page, request.PageSize, ct);
        return Result<PagedResponse<MembershipFreezeReviewSummaryResponse>>.Success(new(
            request.Page, request.PageSize, count, items.Select(row =>
                Summary(row, row.Request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending)).ToArray()));
    }

    public async Task<Result<MembershipFreezeReviewDetailResponse>> GetAsync(int requestId, string type, CancellationToken ct)
    {
        if (type is not (MembershipFreezeReviewTypes.Freeze or MembershipFreezeReviewTypes.EarlyUnfreeze))
            return Result<MembershipFreezeReviewDetailResponse>.Failure("Loại yêu cầu không hợp lệ.");
        var early = type == MembershipFreezeReviewTypes.EarlyUnfreeze;
        var row = await repository.FindAsync(requestId, early, ct);
        if (row is null)
            return NotFound();
        var membership = await requests.GetOwnedMembershipAsync(row.Membership.MemberUserId, row.Request.MemberMembershipId, false, ct);
        if (membership is null || row.Request.RequestedByUserId != membership.MemberUserId)
            return NotFound();
        return Result<MembershipFreezeReviewDetailResponse>.Success(await DetailAsync(row with { Membership = membership }, early, ct));
    }

    public Task<Result<MembershipFreezeReviewDetailResponse>> ApproveAsync(
        string managerId, int requestId, bool early, string? ipAddress, CancellationToken ct) =>
        DecideAsync(managerId, requestId, early, approve: true, reason: null, ipAddress, ct);

    public async Task<Result<MembershipFreezeReviewDetailResponse>> RejectAsync(
        string managerId, int requestId, bool early, RejectMembershipFreezeRequest request, string? ipAddress, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result<MembershipFreezeReviewDetailResponse>.Failure(
                string.Join(" ", validation.Errors.Select(error => error.ErrorMessage).Distinct()));
        return await DecideAsync(managerId, requestId, early, approve: false, request.Reason!.Trim(), ipAddress, ct);
    }

    private async Task<Result<MembershipFreezeReviewDetailResponse>> DecideAsync(
        string managerId, int requestId, bool early, bool approve, string? reason, string? ipAddress, CancellationToken ct)
    {
        var row = await repository.FindAsync(requestId, early, ct);
        if (row is null)
            return NotFound();
        var result = NotFound();
        await policies.ExecuteInSerializableTransactionAsync(async () =>
        {
            // Same parent lock and acquisition order as UC-15, shared by all decisions for this membership.
            var membership = await requests.GetOwnedMembershipAsync(row.Membership.MemberUserId, row.Request.MemberMembershipId, true, ct);
            var freeze = membership?.MembershipFreezes.SingleOrDefault(item =>
                item.Id == requestId && item.RequestedByUserId == membership.MemberUserId);
            if (membership is null || freeze is null)
                return;
            if (!IsPending(freeze, early))
            {
                result = Conflict("Yêu cầu đã được xử lý hoặc không còn chờ duyệt.", "FREEZE_REVIEW_STATE");
                return;
            }
            var today = Today();
            var check = approve ? CheckApproval(membership, freeze, early, today) : Result<int>.Success(0);
            if (!check.IsSuccess)
            {
                result = Conflict(check.ErrorMessage!, check.ErrorCode!);
                return;
            }

            var before = Describe(membership, freeze);
            var now = clock.GetUtcNow().UtcDateTime;
            if (approve)
            {
                var consumed = MembershipFreezeRequestService.ConsumedDays(membership);
                if (early)
                {
                    var unused = freeze.AppliedExpiryExtensionDays!.Value - check.Data;
                    membership.EndDate = membership.EndDate.AddDays(-unused);
                    membership.TotalFrozenDays = checked((int)(consumed - unused));
                    membership.MembershipStatus = MembershipStatus.Active;
                    freeze.ActualEndDate = today;
                    freeze.EarlyUnfreezeAt = now;
                    freeze.RequestStatus = FreezeRequestStatus.Completed;
                    freeze.AppliedExpiryExtensionDays = check.Data;
                }
                else
                {
                    membership.EndDate = membership.EndDate.AddDays(check.Data);
                    membership.TotalFrozenDays = checked((int)(consumed + check.Data));
                    membership.FreezeCount++;
                    // JOB-03 must activate future approved periods, without applying this extension again.
                    freeze.AppliedExpiryExtensionDays = check.Data;
                    freeze.RequestStatus = FreezeRequestStatus.Approved;
                    if (freeze.RequestedStartDate == today)
                    {
                        membership.MembershipStatus = MembershipStatus.Frozen;
                        freeze.ActualStartDate = today;
                    }
                }
            }
            else if (!early)
                freeze.RequestStatus = FreezeRequestStatus.Rejected;

            if (early)
            {
                freeze.EarlyUnfreezeRequestStatus = approve ? FreezeRequestStatus.Approved : FreezeRequestStatus.Rejected;
                freeze.EarlyUnfreezeReviewedByUserId = managerId;
                freeze.EarlyUnfreezeReviewedAt = now;
                freeze.EarlyUnfreezeReviewNote = reason;
            }
            else
            {
                freeze.ReviewedByUserId = managerId;
                freeze.ReviewedAt = now;
                freeze.ReviewNote = reason;
            }
            var title = early ? "Yêu cầu kết thúc bảo lưu sớm" : "Yêu cầu bảo lưu";
            var message = approve ? title + " đã được duyệt." : title + " bị từ chối. " + reason;
            var notification = AuditedWriter.Notify(membership.MemberUserId, managerId, title, message,
                nameof(MembershipFreeze), NotificationType.FreezeApproval);
            var audit = AuditedWriter.Audit(managerId, AuditActionType.Update, nameof(MembershipFreeze),
                ipAddress, before, Describe(membership, freeze));
            await AuditedWriter.SaveChangedAsync(repository, freeze, audit, [notification], ct);
            result = Result<MembershipFreezeReviewDetailResponse>.Success(await DetailAsync(
                row with { Request = freeze, Membership = membership }, early, ct));
        }, ct);
        return result;
    }

    private async Task<MembershipFreezeReviewDetailResponse> DetailAsync(
        MembershipFreezeReviewReadModel row, bool early, CancellationToken ct)
    {
        var response = Detail(row, early);
        return documents is not null && row.Request.SupportingDocumentId is { } id
            ? response with { SupportingDocument = await documents.GetMetadataAsync(id, ct) } : response;
    }

    private MembershipFreezeReviewDetailResponse Detail(MembershipFreezeReviewReadModel row, bool early)
    {
        var membership = row.Membership;
        var today = Today();
        var snapshot = AppliedPolicy(membership, row.Request);
        var check = CheckApproval(membership, row.Request, early, today);
        var used = MembershipFreezeRequestService.ConsumedDays(membership);
        int? remaining = snapshot is null ? null : (int)Math.Max(0L, snapshot.Quotas[0].MaximumCumulativeFreezeDays - used);
        int? actualDays = early && row.Request.ActualStartDate is { } actualStart && row.Request.ActualEndDate is { } actualEnd
            ? Math.Max(0, actualEnd.DayNumber - actualStart.DayNumber + 1)
            : early && check.IsSuccess ? check.Data : null;
        return new(Summary(row, early), MembershipPurchaseService.ToSummary(new OwnedMembership(
            membership.Id, membership.OrderItem.PackageId, row.PlanName, membership.PackageComponent.ComponentType,
            membership.StartDate, membership.EndDate, membership.MembershipStatus), today),
            row.Request.Reason, row.Request.ReviewNote, Utc(row.Request.ReviewedAt),
            row.Request.EarlyUnfreezeReviewNote, Utc(row.Request.EarlyUnfreezeReviewedAt), remaining,
            MembershipFreezeRequestService.ReadAppliedPolicy(row.Request.AppliedPolicySnapshot),
            row.Request.AppliedExpiryExtensionDays, today,
            actualDays, check.IsSuccess, IsPending(row.Request, early), check.ErrorMessage);
    }

    private static MembershipFreezeReviewSummaryResponse Summary(MembershipFreezeReviewReadModel row, bool early) =>
        new(row.Request.Id, early ? MembershipFreezeReviewTypes.EarlyUnfreeze : MembershipFreezeReviewTypes.Freeze,
            row.MemberName, row.MemberEmail, row.PlanName, row.Request.RequestedStartDate, row.Request.RequestedEndDate,
            Duration(row.Request), Utc(early ? row.Request.EarlyUnfreezeRequestedAt ?? row.Request.CreatedAt : row.Request.CreatedAt)!.Value,
            (early ? row.Request.EarlyUnfreezeRequestStatus : row.Request.RequestStatus)?.ToString() ?? "");

    private static bool IsPending(MembershipFreeze freeze, bool early) => early
        ? freeze.EarlyUnfreezeRequestedAt is not null && freeze.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending
        : freeze.RequestStatus == FreezeRequestStatus.Pending;
    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
    private static int Duration(MembershipFreeze freeze) => freeze.RequestedEndDate.DayNumber - freeze.RequestedStartDate.DayNumber + 1;
    private static DateTimeOffset? Utc(DateTime? date) => date is { } value ? new(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
    private static string Describe(MemberMembership membership, MembershipFreeze freeze) => JsonSerializer.Serialize(new
    {
        freeze.RequestStatus, freeze.EarlyUnfreezeRequestStatus, freeze.AppliedExpiryExtensionDays,
        freeze.ActualStartDate, freeze.ActualEndDate, freeze.EarlyUnfreezeAt,
        freeze.ReviewedByUserId, freeze.ReviewedAt, freeze.ReviewNote,
        freeze.EarlyUnfreezeReviewedByUserId, freeze.EarlyUnfreezeReviewedAt, freeze.EarlyUnfreezeReviewNote,
        membership.EndDate, membership.TotalFrozenDays, membership.FreezeCount, membership.MembershipStatus
    });
    private static Result<MembershipFreezeReviewDetailResponse> Conflict(string message, string code) =>
        Result<MembershipFreezeReviewDetailResponse>.Failure(message, code, 409);
    private static Result<MembershipFreezeReviewDetailResponse> NotFound() =>
        Result<MembershipFreezeReviewDetailResponse>.Failure("Không tìm thấy yêu cầu bảo lưu.", "FREEZE_NOT_FOUND", 404);
}
