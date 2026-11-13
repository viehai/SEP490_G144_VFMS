using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipFreezeReviewTests
{
    private const string MemberId = "member";
    private const string ManagerId = "manager";
    private const string ManagerIpAddress = "192.0.2.17";
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateOnly OriginalExpiry = Today.AddDays(90);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ApprovalChargesInclusiveDaysAndExtendsExpiryOnceForTodayOrFutureStart(int startOffset)
    {
        using var fixture = new ReviewFixture();
        fixture.Request.RequestedStartDate = Today.AddDays(startOffset);
        fixture.Request.RequestedEndDate = Today.AddDays(startOffset + 6);
        var snapshot = fixture.Request.AppliedPolicySnapshot;
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.RequestStatus);
        Assert.Equal(7, fixture.Request.AppliedExpiryExtensionDays);
        Assert.Equal(OriginalExpiry.AddDays(7), fixture.Membership.EndDate);
        Assert.Equal(11, fixture.Membership.TotalFrozenDays);
        Assert.Equal(2, fixture.Membership.FreezeCount);
        Assert.Equal(startOffset == 0 ? MembershipStatus.Frozen : MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(ManagerId, fixture.Request.ReviewedByUserId);
        Assert.Equal(UtcNow.UtcDateTime, fixture.Request.ReviewedAt);
        Assert.Equal(snapshot, fixture.Request.AppliedPolicySnapshot);
        Assert.Equal(7, result.Data!.AppliedExpiryExtensionDays);
        Assert.False(result.Data.CanApprove);
        Assert.False(result.Data.CanReject);
        Assert.Equal(cancellation.Token, fixture.Repository.LastSaveToken);
        Assert.Equal(cancellation.Token, fixture.Policies.LastTransactionToken);
        AssertDecisionTrace(fixture, ManagerId);

        var finalized = StateOf(fixture);
        var repeated = await fixture.Service.ApproveAsync("another-manager", fixture.Request.Id, false, null, default);

        Assert.False(repeated.IsSuccess);
        Assert.Equal(409, repeated.StatusCode);
        Assert.Equal(finalized, StateOf(fixture));
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.Repository.Notifications);
    }

    [Fact]
    public async Task ApprovalUsesTheStoredPolicyWhenCurrentPolicyIsUnavailable()
    {
        using var fixture = new ReviewFixture();
        fixture.Membership.TotalFrozenDays = 0;
        fixture.Request.AppliedPolicySnapshot = PolicySnapshot(7, 7, 7);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, fixture.Membership.TotalFrozenDays);
        Assert.Equal(7, result.Data!.AppliedPolicy!.Policy!.MaximumFreezeDays);
        Assert.Equal(7, Assert.Single(result.Data.AppliedPolicy.Quotas).MaximumCumulativeFreezeDays);
    }

    [Fact]
    public async Task ApprovalAcceptsAFreezeStartingOnExpiryEvenWhenItsEndExtendsBeyondExpiry()
    {
        using var fixture = new ReviewFixture();
        fixture.Request.RequestedStartDate = OriginalExpiry;
        fixture.Request.RequestedEndDate = OriginalExpiry.AddDays(6);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.RequestStatus);
        Assert.Equal(OriginalExpiry.AddDays(6), fixture.Request.RequestedEndDate);
        Assert.Equal(OriginalExpiry.AddDays(7), fixture.Membership.EndDate);
        Assert.Equal(7, fixture.Request.AppliedExpiryExtensionDays);
        Assert.Equal(11, fixture.Membership.TotalFrozenDays);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        AssertDecisionTrace(fixture, ManagerId);
    }

    [Fact]
    public async Task ApprovalRejectsAFreezeStartingAfterMembershipExpiryWithoutChangingState()
    {
        using var fixture = new ReviewFixture();
        fixture.Request.RequestedStartDate = OriginalExpiry.AddDays(1);
        fixture.Request.RequestedEndDate = OriginalExpiry.AddDays(7);
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Empty(fixture.Repository.Notifications);
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public async Task ApprovalRevalidatesDynamicQuotaAtTheExactRemainingBoundary(int consumedDays, bool expected)
    {
        using var fixture = new ReviewFixture();
        fixture.Membership.TotalFrozenDays = consumedDays;
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.Equal(expected, result.IsSuccess);
        if (expected)
        {
            Assert.Equal(30, fixture.Membership.TotalFrozenDays);
            Assert.Equal(OriginalExpiry.AddDays(7), fixture.Membership.EndDate);
        }
        else
        {
            Assert.Equal(before, StateOf(fixture));
            Assert.Empty(fixture.Repository.Audits);
            Assert.Empty(fixture.Repository.Notifications);
        }
    }

    [Fact]
    public async Task ApprovalReconcilesApprovedHistoryWithCountersWithoutDoubleCountingPendingDays()
    {
        using var fixture = new ReviewFixture();
        fixture.Membership.TotalFrozenDays = 2;
        var history = fixture.AddRequest(FreezeRequestStatus.Completed, Today.AddDays(-20), 8);
        history.AppliedExpiryExtensionDays = 8;

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(15, fixture.Membership.TotalFrozenDays);
        Assert.Equal(OriginalExpiry.AddDays(7), fixture.Membership.EndDate);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Approved)]
    [InlineData(FreezeRequestStatus.Rejected)]
    [InlineData(FreezeRequestStatus.Cancelled)]
    [InlineData(FreezeRequestStatus.Active)]
    [InlineData(FreezeRequestStatus.Completed)]
    [InlineData(FreezeRequestStatus.Expired)]
    public async Task FinalizedNormalRequestsRejectFurtherApproveAndRejectDecisions(FreezeRequestStatus status)
    {
        using var fixture = new ReviewFixture();
        fixture.Request.RequestStatus = status;
        var before = StateOf(fixture);

        var approve = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);
        var reject = await fixture.Service.RejectAsync(ManagerId, fixture.Request.Id, false,
            new("Cannot approve this request"), ManagerIpAddress, default);

        Assert.Equal(409, approve.StatusCode);
        Assert.Equal(409, reject.StatusCode);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(MembershipStatus.Scheduled)]
    [InlineData(MembershipStatus.Frozen)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    [InlineData(MembershipStatus.Suspended)]
    public async Task ApprovalRechecksTheMembershipLifecycle(MembershipStatus status)
    {
        using var fixture = new ReviewFixture();
        fixture.Membership.MembershipStatus = status;
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovalRejectsExpiredMembershipsAndStaleRequestedStartDates(bool expiredMembership)
    {
        using var fixture = new ReviewFixture();
        if (expiredMembership)
            fixture.Membership.EndDate = Today.AddDays(-1);
        else
            fixture.Request.RequestedStartDate = Today.AddDays(-1);
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Approved)]
    [InlineData(FreezeRequestStatus.Active)]
    [InlineData(FreezeRequestStatus.Completed)]
    public async Task ApprovalRejectsAnOverlappingEffectiveFreeze(FreezeRequestStatus status)
    {
        using var fixture = new ReviewFixture();
        fixture.AddRequest(status, fixture.Request.RequestedEndDate, 3);
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-json")]
    public async Task LegacyRequestsWithoutAUsableSnapshotRequireRejectionInsteadOfInventedPolicy(string? snapshot)
    {
        using var fixture = new ReviewFixture();
        fixture.Request.AppliedPolicySnapshot = snapshot;
        var before = StateOf(fixture);

        var approve = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.Equal(409, approve.StatusCode);
        Assert.Equal(before, StateOf(fixture));

        var reject = await fixture.Service.RejectAsync(ManagerId, fixture.Request.Id, false,
            new("Request policy must be configured"), ManagerIpAddress, default);

        Assert.True(reject.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Rejected, fixture.Request.RequestStatus);
        Assert.Equal(OriginalExpiry, fixture.Membership.EndDate);
        Assert.Equal(4, fixture.Membership.TotalFrozenDays);
    }

    [Fact]
    public async Task AnExistingExtensionMarkerPreventsAnotherNormalApproval()
    {
        using var fixture = new ReviewFixture();
        fixture.Request.AppliedExpiryExtensionDays = 7;
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task ApprovalRejectsExpiryOverflowBeforeChangingAnyState()
    {
        using var fixture = new ReviewFixture();
        fixture.Membership.EndDate = DateOnly.MaxValue.AddDays(-3);
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task RejectionRecordsTheDecisionAndPreservesMembershipAndQuota()
    {
        using var fixture = new ReviewFixture();

        var result = await fixture.Service.RejectAsync(ManagerId, fixture.Request.Id, false,
            new("  Supporting information is insufficient  "), ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Rejected, fixture.Request.RequestStatus);
        Assert.Equal("Supporting information is insufficient", fixture.Request.ReviewNote);
        Assert.Equal(ManagerId, fixture.Request.ReviewedByUserId);
        Assert.Equal(UtcNow.UtcDateTime, fixture.Request.ReviewedAt);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(OriginalExpiry, fixture.Membership.EndDate);
        Assert.Equal(4, fixture.Membership.TotalFrozenDays);
        Assert.Equal(1, fixture.Membership.FreezeCount);
        Assert.Null(fixture.Request.AppliedExpiryExtensionDays);
        AssertDecisionTrace(fixture, ManagerId);

        var finalized = StateOf(fixture);
        var duplicate = await fixture.Service.RejectAsync("another-manager", fixture.Request.Id, false,
            new("A second reason"), null, default);

        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal(finalized, StateOf(fixture));
        Assert.Single(fixture.Repository.Notifications);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task RejectionRequiresAManagerReason(string? reason)
    {
        using var fixture = new ReviewFixture();
        var before = StateOf(fixture);

        var result = await fixture.Service.RejectAsync(ManagerId, fixture.Request.Id, false, new(reason), ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MutationsRecheckRequestMembershipAndOwnerAssociation(bool wrongMembership)
    {
        using var fixture = new ReviewFixture();
        if (wrongMembership)
            fixture.Request.MemberMembershipId = 999;
        else
            fixture.Request.RequestedByUserId = "another-member";
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task MissingRequestsReturnNotFoundForDetailsAndDecisions()
    {
        using var fixture = new ReviewFixture();

        var read = await fixture.Service.GetAsync(999, MembershipFreezeReviewTypes.Freeze, default);
        var approve = await fixture.Service.ApproveAsync(ManagerId, 999, false, ManagerIpAddress, default);
        var reject = await fixture.Service.RejectAsync(ManagerId, 999, false, new("Not eligible"), ManagerIpAddress, default);

        Assert.Equal(404, read.StatusCode);
        Assert.Equal(404, approve.StatusCode);
        Assert.Equal(404, reject.StatusCode);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task QueueAndDetailDistinguishNormalAndEarlyRequestsWithPolicyAndQuotaContext()
    {
        using var fixture = new ReviewFixture();
        var early = fixture.AddRequest(FreezeRequestStatus.Approved, Today.AddDays(-2), 7);
        early.EarlyUnfreezeRequestedAt = UtcNow.AddHours(-1).UtcDateTime;
        early.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Pending;

        var normalQueue = await fixture.Service.ListAsync(new() { Type = MembershipFreezeReviewTypes.Freeze }, default);
        var earlyQueue = await fixture.Service.ListAsync(new() { Type = MembershipFreezeReviewTypes.EarlyUnfreeze }, default);
        var detail = await fixture.Service.GetAsync(fixture.Request.Id, MembershipFreezeReviewTypes.Freeze, default);

        Assert.True(normalQueue.IsSuccess);
        Assert.Equal(1, normalQueue.Data!.TotalCount);
        Assert.Equal(MembershipFreezeReviewTypes.Freeze, Assert.Single(normalQueue.Data.Items).RequestType);
        Assert.True(earlyQueue.IsSuccess);
        Assert.Equal(early.Id, Assert.Single(earlyQueue.Data!.Items).FreezeRequestId);
        Assert.Equal(MembershipFreezeReviewTypes.EarlyUnfreeze, Assert.Single(earlyQueue.Data.Items).RequestType);
        Assert.True(detail.IsSuccess);
        Assert.Equal("Member name", detail.Data!.Request.MemberName);
        Assert.Equal("member@example.com", detail.Data.Request.MemberEmail);
        Assert.Equal("Original purchased plan", detail.Data.Request.PlanName);
        Assert.Equal(fixture.Membership.StartDate, detail.Data.Membership.StartDate);
        Assert.Equal(OriginalExpiry, detail.Data.Membership.EndDate);
        Assert.Equal(fixture.Request.Reason, detail.Data.Reason);
        Assert.Equal(3, detail.Data.AppliedPolicy!.Policy!.MinimumFreezeDays);
        Assert.Equal(23, detail.Data.RemainingQuota);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task EarlyApprovalIncludesStartAndUnfreezeDatesAndReconcilesExpiryAndQuotaOnce(int elapsedDays)
    {
        using var fixture = new ReviewFixture();
        fixture.PrepareEarlyRequest(elapsedDays);
        var actualDays = elapsedDays + 1;

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, true, ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.EarlyUnfreezeRequestStatus);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(Today, fixture.Request.ActualEndDate);
        Assert.Equal(UtcNow.UtcDateTime, fixture.Request.EarlyUnfreezeAt);
        Assert.Equal(actualDays, fixture.Request.AppliedExpiryExtensionDays);
        Assert.Equal(OriginalExpiry.AddDays(actualDays), fixture.Membership.EndDate);
        Assert.Equal(4 + actualDays, fixture.Membership.TotalFrozenDays);
        Assert.Equal(2, fixture.Membership.FreezeCount);
        Assert.Equal("original-manager", fixture.Request.ReviewedByUserId);
        Assert.Equal(ManagerId, fixture.Request.EarlyUnfreezeReviewedByUserId);
        Assert.Equal(UtcNow.UtcDateTime, fixture.Request.EarlyUnfreezeReviewedAt);
        AssertDecisionTrace(fixture, ManagerId);

        var finalized = StateOf(fixture);
        var repeated = await fixture.Service.ApproveAsync("another-manager", fixture.Request.Id, true, null, default);

        Assert.Equal(409, repeated.StatusCode);
        Assert.Equal(finalized, StateOf(fixture));
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.Repository.Notifications);
    }

    [Fact]
    public async Task EarlyRejectionPreservesTheOriginalApprovedFreezeAndItsExtension()
    {
        using var fixture = new ReviewFixture();
        fixture.PrepareEarlyRequest();
        var startDate = fixture.Request.ActualStartDate;

        var result = await fixture.Service.RejectAsync(ManagerId, fixture.Request.Id, true,
            new("  Continue the approved freeze  "), ManagerIpAddress, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Rejected, fixture.Request.EarlyUnfreezeRequestStatus);
        Assert.Equal("Continue the approved freeze", fixture.Request.EarlyUnfreezeReviewNote);
        Assert.Equal(ManagerId, fixture.Request.EarlyUnfreezeReviewedByUserId);
        Assert.Equal(UtcNow.UtcDateTime, fixture.Request.EarlyUnfreezeReviewedAt);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.RequestStatus);
        Assert.Equal("original-manager", fixture.Request.ReviewedByUserId);
        Assert.Equal(MembershipStatus.Frozen, fixture.Membership.MembershipStatus);
        Assert.Equal(OriginalExpiry.AddDays(10), fixture.Membership.EndDate);
        Assert.Equal(14, fixture.Membership.TotalFrozenDays);
        Assert.Equal(10, fixture.Request.AppliedExpiryExtensionDays);
        Assert.Equal(startDate, fixture.Request.ActualStartDate);
        Assert.Null(fixture.Request.ActualEndDate);
        Assert.Null(fixture.Request.EarlyUnfreezeAt);
        AssertDecisionTrace(fixture, ManagerId);
    }

    [Theory]
    [InlineData(MembershipStatus.Active, FreezeRequestStatus.Approved, -4, 10, 10)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Pending, -4, 10, 10)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Approved, 1, 10, 10)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Approved, -4, 5, 5)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Approved, -4, 10, null)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Approved, -4, 10, 9)]
    public async Task EarlyApprovalRequiresAnEffectiveFreezeAndItsOriginalAppliedExtension(
        MembershipStatus membershipStatus, FreezeRequestStatus requestStatus, int startOffset, int duration, int? extension)
    {
        using var fixture = new ReviewFixture();
        fixture.PrepareEarlyRequest();
        fixture.Membership.MembershipStatus = membershipStatus;
        fixture.Request.RequestStatus = requestStatus;
        fixture.Request.RequestedStartDate = Today.AddDays(startOffset);
        fixture.Request.ActualStartDate = Today.AddDays(startOffset);
        fixture.Request.RequestedEndDate = Today.AddDays(startOffset + duration - 1);
        fixture.Request.AppliedExpiryExtensionDays = extension;
        var before = StateOf(fixture);

        var result = await fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, true, ManagerIpAddress, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateOf(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingNormalDecisionsHaveExactlyOneWinner(bool secondRejects)
    {
        using var fixture = new ReviewFixture();
        var approve = fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, false, ManagerIpAddress, default);
        var competing = secondRejects
            ? fixture.Service.RejectAsync("another-manager", fixture.Request.Id, false, new("Declined"), null, default)
            : fixture.Service.ApproveAsync("another-manager", fixture.Request.Id, false, null, default);

        var results = await Task.WhenAll(approve, competing);

        Assert.Single(results.Where(result => result.IsSuccess));
        Assert.Equal(409, Assert.Single(results.Where(result => !result.IsSuccess)).StatusCode);
        Assert.Equal(2, fixture.Policies.TransactionAttempts);
        var approved = fixture.Request.RequestStatus == FreezeRequestStatus.Approved;
        Assert.Equal(approved ? OriginalExpiry.AddDays(7) : OriginalExpiry, fixture.Membership.EndDate);
        Assert.Equal(approved ? 11 : 4, fixture.Membership.TotalFrozenDays);
        Assert.Equal(approved ? 2 : 1, fixture.Membership.FreezeCount);
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.Repository.Notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingEarlyDecisionsReconcileAtMostOnce(bool secondRejects)
    {
        using var fixture = new ReviewFixture();
        fixture.PrepareEarlyRequest();
        var approve = fixture.Service.ApproveAsync(ManagerId, fixture.Request.Id, true, ManagerIpAddress, default);
        var competing = secondRejects
            ? fixture.Service.RejectAsync("another-manager", fixture.Request.Id, true, new("Continue freezing"), null, default)
            : fixture.Service.ApproveAsync("another-manager", fixture.Request.Id, true, null, default);

        var results = await Task.WhenAll(approve, competing);

        Assert.Single(results.Where(result => result.IsSuccess));
        Assert.Equal(409, Assert.Single(results.Where(result => !result.IsSuccess)).StatusCode);
        Assert.Equal(2, fixture.Policies.TransactionAttempts);
        var approved = fixture.Request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Approved;
        Assert.Equal(OriginalExpiry.AddDays(approved ? 5 : 10), fixture.Membership.EndDate);
        Assert.Equal(approved ? 9 : 14, fixture.Membership.TotalFrozenDays);
        Assert.Equal(2, fixture.Membership.FreezeCount);
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.Repository.Notifications);
    }

    private static void AssertDecisionTrace(ReviewFixture fixture, string managerId)
    {
        var audit = Assert.Single(fixture.Repository.Audits);
        Assert.Equal(managerId, audit.ActorUserId);
        Assert.Equal(ManagerIpAddress, audit.IpAddress);
        Assert.Equal(AuditActionType.Update, audit.ActionType);
        Assert.Equal(nameof(MembershipFreeze), audit.AuditEntity);
        Assert.Equal(fixture.Request.Id, audit.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValue));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValue));
        Assert.NotEqual(audit.OldValue, audit.NewValue);
        var notification = Assert.Single(fixture.Repository.Notifications);
        Assert.Equal(MemberId, notification.RecipientUserId);
        Assert.Equal(managerId, notification.SenderUserId);
        Assert.Equal(nameof(NotificationType.FreezeApproval), notification.NotificationType);
        Assert.Equal(nameof(MembershipFreeze), notification.EntityType);
        Assert.Equal(fixture.Request.Id, notification.EntityId);
    }

    private static string StateOf(ReviewFixture fixture) => JsonSerializer.Serialize(new
    {
        fixture.Membership.MembershipStatus,
        fixture.Membership.EndDate,
        fixture.Membership.TotalFrozenDays,
        fixture.Membership.FreezeCount,
        fixture.Request.RequestStatus,
        fixture.Request.ActualStartDate,
        fixture.Request.ActualEndDate,
        fixture.Request.ReviewedByUserId,
        fixture.Request.ReviewedAt,
        fixture.Request.ReviewNote,
        fixture.Request.AppliedExpiryExtensionDays,
        fixture.Request.EarlyUnfreezeAt,
        fixture.Request.EarlyUnfreezeRequestStatus,
        fixture.Request.EarlyUnfreezeReviewedByUserId,
        fixture.Request.EarlyUnfreezeReviewedAt,
        fixture.Request.EarlyUnfreezeReviewNote
    });

    private static string PolicySnapshot(int minimum = 3, int maximum = 10, int quota = 30) =>
        JsonSerializer.Serialize(new MembershipFreezePolicyResponse(
            new(8, minimum, maximum, 3, 14, UtcNow.AddDays(-4).UtcDateTime),
            [new(180, MembershipFreezeTierUnits.Days, quota)]));

    private sealed class ReviewFixture : IDisposable
    {
        public MemberMembership Membership { get; } = new()
        {
            Id = 1,
            MemberUserId = MemberId,
            MembershipStatus = MembershipStatus.Active,
            StartDate = Today.AddDays(-30),
            EndDate = OriginalExpiry,
            TotalFrozenDays = 4,
            FreezeCount = 1,
            PackageComponentId = 17,
            PackageComponent = new() { Id = 17, ComponentType = ComponentType.GYM, DurationDays = 180 },
            OrderItem = new()
            {
                PackageId = 4,
                PackageNameSnapshot = "Original purchased plan",
                ComponentTermsSnapshot = JsonSerializer.Serialize(new[] { new { Id = 17, Type = ComponentType.GYM, Days = 180 } })
            }
        };
        public MembershipFreeze Request { get; }
        public PolicyRepositoryStub Policies { get; } = new();
        public ReviewRepositoryStub Repository { get; }
        public MembershipFreezeReviewService Service { get; }

        public ReviewFixture()
        {
            Request = AddRequest(FreezeRequestStatus.Pending, Today.AddDays(1), 7);
            Repository = new(Membership, Policies);
            Service = new(Repository, new RequestRepositoryStub(Membership), Policies,
                new RejectMembershipFreezeRequestValidator(), new TestClock());
        }

        public MembershipFreeze AddRequest(FreezeRequestStatus status, DateOnly start, int days)
        {
            var request = new MembershipFreeze
            {
                Id = Membership.MembershipFreezes.Count + 1,
                MemberMembershipId = Membership.Id,
                MemberMembership = Membership,
                RequestedByUserId = MemberId,
                RequestedStartDate = start,
                RequestedEndDate = start.AddDays(days - 1),
                RequestStatus = status,
                Reason = "Travel plans",
                FreezePolicyId = 8,
                AppliedPolicySnapshot = PolicySnapshot(),
                CreatedAt = UtcNow.AddDays(-4).UtcDateTime
            };
            Membership.MembershipFreezes.Add(request);
            return request;
        }

        public void PrepareEarlyRequest(int elapsedDays = 4)
        {
            Request.RequestStatus = FreezeRequestStatus.Approved;
            Request.RequestedStartDate = Today.AddDays(-elapsedDays);
            Request.RequestedEndDate = Request.RequestedStartDate.AddDays(9);
            Request.ActualStartDate = Request.RequestedStartDate;
            Request.AppliedExpiryExtensionDays = 10;
            Request.ReviewedByUserId = "original-manager";
            Request.ReviewedAt = UtcNow.AddDays(-elapsedDays).UtcDateTime;
            Request.EarlyUnfreezeRequestedAt = UtcNow.AddHours(-1).UtcDateTime;
            Request.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Pending;
            Membership.MembershipStatus = MembershipStatus.Frozen;
            Membership.EndDate = OriginalExpiry.AddDays(10);
            Membership.TotalFrozenDays = 14;
            Membership.FreezeCount = 2;
        }

        public void Dispose() => Policies.Dispose();
    }

    private sealed class ReviewRepositoryStub(MemberMembership membership, PolicyRepositoryStub policies)
        : IMembershipFreezeReviewRepository
    {
        public List<AuditLog> Audits { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public int SaveCount { get; private set; }
        public CancellationToken LastSaveToken { get; private set; }

        public Task<(IReadOnlyList<MembershipFreezeReviewReadModel> Items, int TotalCount)> SearchAsync(
            string? type, int page, int pageSize, CancellationToken ct)
        {
            var matches = membership.MembershipFreezes.Where(request => type == MembershipFreezeReviewTypes.EarlyUnfreeze
                ? request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending
                : request.RequestStatus == FreezeRequestStatus.Pending).Select(ToReadModel).ToArray();
            return Task.FromResult(((IReadOnlyList<MembershipFreezeReviewReadModel>)matches
                .Skip((page - 1) * pageSize).Take(pageSize).ToArray(), matches.Length));
        }

        public Task<MembershipFreezeReviewReadModel?> FindAsync(int requestId, bool early, CancellationToken ct)
        {
            var request = membership.MembershipFreezes.SingleOrDefault(request => request.Id == requestId &&
                (!early || request.EarlyUnfreezeRequestedAt is not null));
            return Task.FromResult(request is null ? null : ToReadModel(request));
        }

        private MembershipFreezeReviewReadModel ToReadModel(MembershipFreeze request) =>
            new(request, "Member name", "member@example.com", membership.OrderItem.PackageNameSnapshot, membership);

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            Assert.True(policies.TransactionOpen);
            if (entity is AuditLog audit)
                Audits.Add(audit);
            else
                Notifications.Add(Assert.IsType<Notification>(entity));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(policies.TransactionOpen);
            Assert.Equal(policies.LastTransactionToken, cancellationToken);
            SaveCount++;
            LastSaveToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RequestRepositoryStub(MemberMembership membership) : IMembershipFreezeRequestRepository
    {
        public Task<MemberMembership?> GetOwnedMembershipAsync(
            string memberId, int membershipId, bool forUpdate, CancellationToken ct) =>
            Task.FromResult(memberId == membership.MemberUserId && membershipId == membership.Id ? membership : null);
        public Task<List<MembershipFreezeReadModel>> ListOwnedRequestsAsync(string memberId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<MembershipFreezeReadModel?> FindOwnedRequestAsync(string memberId, int requestId, CancellationToken ct) =>
            throw new NotSupportedException();
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PolicyRepositoryStub : IMembershipFreezePolicyRepository, IDisposable
    {
        private readonly SemaphoreSlim _transaction = new(1, 1);
        private int _transactionAttempts;
        public int TransactionAttempts => _transactionAttempts;
        public bool TransactionOpen { get; private set; }
        public CancellationToken LastTransactionToken { get; private set; }

        public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
        {
            Interlocked.Increment(ref _transactionAttempts);
            await _transaction.WaitAsync(ct);
            TransactionOpen = true;
            LastTransactionToken = ct;
            try
            {
                // Keep the lock while yielding so a competing review reads before its transaction can enter.
                await Task.Yield();
                await work();
            }
            finally
            {
                TransactionOpen = false;
                _transaction.Release();
            }
        }

        public Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct) =>
            throw new NotSupportedException("Review must use the policy captured with the request.");
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() => _transaction.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
