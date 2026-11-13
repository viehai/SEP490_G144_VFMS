using System.Text.Json;
using FluentValidation;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipFreezeRequestService(
    IMembershipFreezeRequestRepository repository, IMembershipFreezePolicyRepository policies,
    IValidator<MembershipFreezeRequest> validator, TimeProvider clock, IMembershipFreezeDocumentStore? documents = null) : IMembershipFreezeRequestService
{
    public async Task<Result<IReadOnlyList<MembershipFreezeRequestResponse>>> ListAsync(string memberId, CancellationToken ct)
    {
        var requests = await repository.ListOwnedRequestsAsync(memberId, ct);
        var today = LocalToday();
        var ids = requests.Select(row => row.Request.SupportingDocumentId).OfType<Guid>().Distinct().ToArray();
        var metadata = documents is not null && ids.Length > 0
            ? await documents.GetMetadataAsync(ids, ct) : new Dictionary<Guid, MembershipFreezeDocumentResponse>();
        return Result<IReadOnlyList<MembershipFreezeRequestResponse>>.Success(requests
            .Select(row => ToResponse(row.Request, row.PlanName, row.MembershipStatus, today) with
            {
                SupportingDocument = row.Request.SupportingDocumentId is { } id ? metadata.GetValueOrDefault(id) : null
            }).ToArray());
    }

    public async Task<Result<MembershipFreezeRequestResponse>> GetAsync(string memberId, int requestId, CancellationToken ct)
    {
        var row = await repository.FindOwnedRequestAsync(memberId, requestId, ct);
        return row is null ? NotFound<MembershipFreezeRequestResponse>()
            : Result<MembershipFreezeRequestResponse>.Success(await ToResponseAsync(row.Request, row.PlanName, row.MembershipStatus, LocalToday(), ct));
    }

    public async Task<Result<MembershipFreezeRequestResponse>> CreateAsync(
        string memberId, MembershipFreezeRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var result = NotFound<MembershipFreezeRequestResponse>();
        await policies.ExecuteInSerializableTransactionAsync(async () =>
        {
            var membership = await repository.GetOwnedMembershipAsync(memberId, request.MembershipId!.Value, true, ct);
            if (membership is null)
                return;
            result = await SaveRequestAsync(memberId, membership, request, existing: null, ct);
        }, ct);
        return result;
    }

    public async Task<Result<MembershipFreezeRequestResponse>> UpdateAsync(
        string memberId, int requestId, MembershipFreezeRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return await MutateExistingAsync(memberId, requestId, async (membership, existing) =>
        {
            if (existing.RequestStatus != FreezeRequestStatus.Pending)
                return StateConflict();
            if (request.MembershipId != membership.Id)
                return Result<MembershipFreezeRequestResponse>.Failure("Không thể đổi gói hội viên của yêu cầu.", "FREEZE_MEMBERSHIP_MISMATCH");
            return await SaveRequestAsync(memberId, membership, request, existing, ct);
        }, ct);
    }

    public Task<Result<MembershipFreezeRequestResponse>> CancelAsync(string memberId, int requestId, CancellationToken ct) =>
        MutateExistingAsync(memberId, requestId, async (membership, request) =>
        {
            if (request.RequestStatus != FreezeRequestStatus.Pending)
                return StateConflict();
            request.RequestStatus = FreezeRequestStatus.Cancelled;
            await repository.SaveChangesAsync(ct);
            return Result<MembershipFreezeRequestResponse>.Success(await ToResponseAsync(request,
                membership.OrderItem.PackageNameSnapshot, membership.MembershipStatus, LocalToday(), ct));
        }, ct);

    public Task<Result<MembershipFreezeRequestResponse>> RequestEarlyUnfreezeAsync(
        string memberId, int requestId, CancellationToken ct) =>
        MutateExistingAsync(memberId, requestId, async (membership, request) =>
        {
            if (request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending)
                return Result<MembershipFreezeRequestResponse>.Success(await ToResponseAsync(request,
                    membership.OrderItem.PackageNameSnapshot, membership.MembershipStatus, LocalToday(), ct));
            if (!CanRequestEarlyUnfreeze(request, membership.MembershipStatus, LocalToday()))
                return StateConflict();
            request.EarlyUnfreezeRequestedAt = clock.GetUtcNow().UtcDateTime;
            request.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Pending;
            request.EarlyUnfreezeReviewedByUserId = null;
            request.EarlyUnfreezeReviewedAt = null;
            request.EarlyUnfreezeReviewNote = null;
            await repository.SaveChangesAsync(ct);
            return Result<MembershipFreezeRequestResponse>.Success(await ToResponseAsync(request,
                membership.OrderItem.PackageNameSnapshot, membership.MembershipStatus, LocalToday(), ct));
        }, ct);

    private async Task<Result<MembershipFreezeRequestResponse>> SaveRequestAsync(
        string memberId, MemberMembership membership, MembershipFreezeRequest input, MembershipFreeze? existing, CancellationToken ct)
    {
        var policy = await policies.GetActiveGlobalAsync(false, ct);
        var eligibility = Evaluate(membership, policy, LocalToday());
        if (!eligibility.Eligible)
            return Ineligible<MembershipFreezeRequestResponse>(eligibility);
        var period = ValidatePeriod(membership, input, eligibility, existing?.Id);
        if (!period.IsSuccess)
            return Result<MembershipFreezeRequestResponse>.Failure(period.ErrorMessage!, period.ErrorCode!, period.StatusCode);

        var request = existing ?? new MembershipFreeze
        {
            MemberMembershipId = membership.Id, RequestedByUserId = memberId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        request.RequestedStartDate = input.StartDate!.Value;
        request.RequestedEndDate = period.Data;
        request.Reason = input.Reason!.Trim();
        request.FreezePolicyId = policy!.Id;
        var (days, months) = MembershipPurchaseService.PurchasedDuration(membership.OrderItem.ComponentTermsSnapshot,
            membership.PackageComponentId, membership.PackageComponent.DurationDays, membership.PackageComponent.DurationMonths);
        var tier = policy.TierQuotas.Single(quota => quota.Unit == (days is > 0 ? FreezeTierUnit.Days : FreezeTierUnit.Months) &&
            quota.Duration == (days is > 0 ? days : months));
        request.AppliedPolicySnapshot = JsonSerializer.Serialize(
            new MembershipFreezePolicyResponse(eligibility.Policy, [new(tier.Duration, tier.Unit.ToString(), tier.MaximumCumulativeFreezeDays)]));
        if (existing is null)
            repository.Add(request);
        await repository.SaveChangesAsync(ct);
        return Result<MembershipFreezeRequestResponse>.Success(await ToResponseAsync(request,
            membership.OrderItem.PackageNameSnapshot, membership.MembershipStatus, eligibility.Today, ct), existing is null ? 201 : 200);
    }

    private async Task<Result<MembershipFreezeRequestResponse>> MutateExistingAsync(
        string memberId, int requestId,
        Func<MemberMembership, MembershipFreeze, Task<Result<MembershipFreezeRequestResponse>>> action, CancellationToken ct)
    {
        // Resolve the immutable parent before the transaction so competing request mutations lock in the same order.
        var row = await repository.FindOwnedRequestAsync(memberId, requestId, ct);
        if (row is null)
            return NotFound<MembershipFreezeRequestResponse>();
        var result = NotFound<MembershipFreezeRequestResponse>();
        await policies.ExecuteInSerializableTransactionAsync(async () =>
        {
            var membership = await repository.GetOwnedMembershipAsync(memberId, row.Request.MemberMembershipId, true, ct);
            var request = membership?.MembershipFreezes.SingleOrDefault(
                freeze => freeze.Id == requestId && freeze.RequestedByUserId == memberId);
            if (membership is not null && request is not null)
                result = await action(membership, request);
        }, ct);
        return result;
    }

    private DateOnly LocalToday() => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);

    private async Task<MembershipFreezeRequestResponse> ToResponseAsync(
        MembershipFreeze request, string planName, MembershipStatus status, DateOnly today, CancellationToken ct)
    {
        var response = ToResponse(request, planName, status, today);
        return documents is not null && request.SupportingDocumentId is { } id
            ? response with { SupportingDocument = await documents.GetMetadataAsync(id, ct) } : response;
    }

    private static MembershipFreezeRequestResponse ToResponse(
        MembershipFreeze request, string planName, MembershipStatus membershipStatus, DateOnly today) =>
        new(request.Id, request.MemberMembershipId, planName, request.RequestedStartDate, request.RequestedEndDate,
            request.RequestedEndDate.DayNumber - request.RequestedStartDate.DayNumber + 1, request.Reason, Utc(request.CreatedAt),
            request.RequestStatus.ToString(), request.ReviewNote, request.ReviewedAt is { } reviewed ? Utc(reviewed) : null,
            request.EarlyUnfreezeRequestedAt is { } early ? Utc(early) : null, request.EarlyUnfreezeRequestStatus?.ToString(),
            request.RequestStatus == FreezeRequestStatus.Pending, request.RequestStatus == FreezeRequestStatus.Pending,
            CanRequestEarlyUnfreeze(request, membershipStatus, today), ReadAppliedPolicy(request.AppliedPolicySnapshot),
            request.EarlyUnfreezeReviewNote,
            request.EarlyUnfreezeReviewedAt is { } earlyReviewed ? Utc(earlyReviewed) : null);

    private static bool CanRequestEarlyUnfreeze(MembershipFreeze request, MembershipStatus membershipStatus, DateOnly today) =>
        membershipStatus == MembershipStatus.Frozen &&
        request.RequestStatus is FreezeRequestStatus.Active or FreezeRequestStatus.Approved &&
        (request.ActualStartDate ?? request.RequestedStartDate) <= today &&
        today < (request.ActualEndDate ?? request.RequestedEndDate) &&
        request.EarlyUnfreezeAt is null &&
        request.EarlyUnfreezeRequestStatus is null or FreezeRequestStatus.Rejected;

    internal static MembershipFreezePolicyResponse? ReadAppliedPolicy(string? snapshot)
    {
        if (snapshot is null)
            return null;
        try { return JsonSerializer.Deserialize<MembershipFreezePolicyResponse>(snapshot); }
        catch (JsonException) { return null; }
    }

    private static DateTimeOffset Utc(DateTime timestamp) => new(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
    private static Result<T> NotFound<T>() => Result<T>.Failure("Không tìm thấy gói hội viên hoặc yêu cầu bảo lưu.", "FREEZE_NOT_FOUND", 404);
    private static Result<MembershipFreezeRequestResponse> StateConflict() =>
        Result<MembershipFreezeRequestResponse>.Failure("Trạng thái hiện tại không cho phép thao tác này.", "FREEZE_REQUEST_STATE", 409);
    private static Result<MembershipFreezeRequestResponse> Invalid(IEnumerable<string> messages) =>
        Result<MembershipFreezeRequestResponse>.Failure(string.Join(" ", messages.Distinct()));
}
