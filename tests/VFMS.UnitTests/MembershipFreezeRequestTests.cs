using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed partial class MembershipFreezeRequestTests
{
    private const string MemberId = "member";
    private const string OtherMemberId = "another-member";
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public async Task ValidRequestIsPendingAndPreservesMembershipWhileCapturingPolicy()
    {
        var fixture = new FreezeFixture();
        var before = MembershipState(fixture.Membership);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.CreateAsync(MemberId, Request() with { Reason = "  Medical leave  " }, cancellation.Token);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<MembershipFreezeRequestResponse>(result.Data);
        var stored = Assert.Single(fixture.Membership.MembershipFreezes);
        Assert.Equal(FreezeRequestStatus.Pending, stored.RequestStatus);
        Assert.Equal("Pending", response.Status);
        Assert.Equal(MemberId, stored.RequestedByUserId);
        Assert.Equal(fixture.Membership.Id, stored.MemberMembershipId);
        Assert.Equal(Today.AddDays(3), stored.RequestedStartDate);
        Assert.Equal(Today.AddDays(5), stored.RequestedEndDate);
        Assert.Equal(3, response.RequestedDays);
        Assert.Equal("Medical leave", stored.Reason);
        Assert.Equal(fixture.Policies.Policy!.Id, stored.FreezePolicyId);
        Assert.False(string.IsNullOrWhiteSpace(stored.AppliedPolicySnapshot));
        Assert.Equal(3, response.AppliedPolicy!.Policy!.MinimumFreezeDays);
        Assert.Equal(10, response.AppliedPolicy.Policy.MaximumFreezeDays);
        Assert.Null(stored.ActualStartDate);
        Assert.Null(stored.ActualEndDate);
        Assert.Equal(before, MembershipState(fixture.Membership));
        Assert.Equal(cancellation.Token, fixture.Repository.LastToken);
        Assert.Equal(cancellation.Token, fixture.Policies.LastToken);
        Assert.Equal(cancellation.Token, fixture.Repository.LastSaveToken);
    }

    [Theory]
    [InlineData(MemberId, 999)]
    [InlineData(OtherMemberId, 1)]
    public async Task MissingAndOtherMembersMembershipsReturnNotFound(string memberId, int membershipId)
    {
        var fixture = new FreezeFixture();

        var result = await fixture.Service.CreateAsync(memberId, Request() with { MembershipId = membershipId }, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(fixture.Membership.MembershipFreezes);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(MembershipStatus.Scheduled)]
    [InlineData(MembershipStatus.Frozen)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    [InlineData(MembershipStatus.Suspended)]
    public async Task OnlyActiveMembershipsCanSubmitNormalRequests(MembershipStatus status)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.MembershipStatus = status;

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(fixture.Membership.MembershipFreezes);
    }

    [Fact]
    public async Task AnActiveRecordPastExpiryIsIneligible()
    {
        var fixture = new FreezeFixture();
        fixture.Membership.EndDate = Today.AddDays(-1);

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(fixture.Membership.MembershipFreezes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnconfiguredPolicyPreventsSubmission(bool legacy)
    {
        var fixture = new FreezeFixture();
        if (legacy)
            fixture.Policies.Policy!.MinFreezeDaysPerRequest = null;
        else
            fixture.Policies.Policy = null;

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(13, false)]
    [InlineData(14, true)]
    public async Task PaidDayEligibilityCountsElapsedGymCalendarDaysAtSubmission(int elapsedDays, bool expected)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.StartDate = Today.AddDays(-elapsedDays);

        var eligibility = await fixture.Service.GetEligibilityAsync(MemberId, 1, null, default);
        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.True(eligibility.IsSuccess);
        Assert.Equal(elapsedDays, eligibility.Data!.PaidDays);
        Assert.Equal(expected, eligibility.Data.Eligible);
        Assert.Equal(expected, result.IsSuccess);
    }

    [Fact]
    public async Task ActivationDayIsDayZeroEvenWhenTheRequestedFreezeStartsLater()
    {
        var fixture = new FreezeFixture();
        fixture.Membership.StartDate = Today;
        fixture.Policies.Policy!.EligibilityPaidDays = 1;

        var result = await fixture.Service.CreateAsync(MemberId, Request() with { StartDate = Today.AddDays(10) }, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task PerRequestMinimumAndMaximumHaveInclusiveBoundaries(int days, bool expected)
    {
        var fixture = new FreezeFixture();

        var result = await fixture.Service.CreateAsync(MemberId, Request(days), default);

        Assert.Equal(expected, result.IsSuccess);
        if (expected)
            Assert.Equal(Today.AddDays(3 + days - 1), result.Data!.EndDate);
        else
            Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task AdvanceNoticeUsesTheCurrentGymDateAndAnInclusiveBoundary(int startOffset, bool expected)
    {
        var fixture = new FreezeFixture();

        var eligibility = await fixture.Service.GetEligibilityAsync(MemberId, 1, null, default);
        var result = await fixture.Service.CreateAsync(MemberId, Request() with { StartDate = Today.AddDays(startOffset) }, default);

        Assert.Equal(Today, eligibility.Data!.Today);
        Assert.Equal(Today.AddDays(3), eligibility.Data.EarliestStartDate);
        Assert.Equal(expected, result.IsSuccess);
    }

    [Fact]
    public async Task AFreezeMayFinishAfterTheOriginalMembershipExpiry()
    {
        var fixture = new FreezeFixture();
        fixture.Membership.EndDate = Today.AddDays(4);
        var before = MembershipState(fixture.Membership);

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Today.AddDays(5), result.Data!.EndDate);
        Assert.Equal(before, MembershipState(fixture.Membership));
    }

    [Theory]
    [InlineData("Days", FreezeTierUnit.Days, 180)]
    [InlineData("DurationDays", FreezeTierUnit.Days, 180)]
    [InlineData("Months", FreezeTierUnit.Months, 6)]
    [InlineData("DurationMonths", FreezeTierUnit.Months, 6)]
    public async Task TierUsesOriginalPurchaseTermsIncludingLegacySnapshotNames(
        string durationProperty, FreezeTierUnit unit, int duration)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.OrderItem.ComponentTermsSnapshot = Snapshot(durationProperty, duration);
        fixture.Membership.PackageComponent.DurationDays = 9;
        fixture.Membership.PackageComponent.DurationMonths = null;
        fixture.Policies.Policy!.TierQuotas =
            [new() { Duration = duration, Unit = unit, MaximumCumulativeFreezeDays = 30 }];

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task LegacyMembershipWithoutASnapshotUsesItsComponentTerm()
    {
        var fixture = new FreezeFixture();
        fixture.Membership.OrderItem.ComponentTermsSnapshot = null;

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AnUnmatchedTierCannotBorrowAnotherTiersQuota()
    {
        var fixture = new FreezeFixture();
        fixture.Policies.Policy!.TierQuotas.Single().Duration = 365;

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(ComponentType.PT, 180)]
    [InlineData(ComponentType.YOGA, 7)]
    public async Task PtAndWeeklyYogaEntitlementsCannotUseMembershipFreezes(ComponentType componentType, int duration)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.PackageComponent.ComponentType = componentType;
        fixture.Membership.PackageComponent.DurationDays = duration;
        fixture.Membership.OrderItem.ComponentTermsSnapshot = Snapshot("Days", duration, componentType);
        fixture.Policies.Policy!.TierQuotas.Single().Duration = duration;

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Pending, 2, 28)]
    [InlineData(FreezeRequestStatus.Rejected, 2, 28)]
    [InlineData(FreezeRequestStatus.Cancelled, 2, 28)]
    [InlineData(FreezeRequestStatus.Expired, 2, 28)]
    [InlineData(FreezeRequestStatus.Approved, 2, 25)]
    [InlineData(FreezeRequestStatus.Active, 2, 25)]
    [InlineData(FreezeRequestStatus.Completed, 2, 25)]
    [InlineData(FreezeRequestStatus.Completed, 8, 22)]
    public async Task QuotaUsesApprovedUsageAndPersistedTotalsWithoutDoubleCounting(
        FreezeRequestStatus status, int totalFrozenDays, int expectedRemaining)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.TotalFrozenDays = totalFrozenDays;
        fixture.AddRequest(status, Today.AddDays(-8), 5);

        var result = await fixture.Service.GetEligibilityAsync(MemberId, 1, null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedRemaining, result.Data!.RemainingQuota);
    }

    [Fact]
    public async Task CompletedUsageUsesActualDatesAndPendingRequestsDoNotReserveQuota()
    {
        var fixture = new FreezeFixture();
        var completed = fixture.AddRequest(FreezeRequestStatus.Completed, Today.AddDays(-10), 6);
        completed.ActualStartDate = Today.AddDays(-10);
        completed.ActualEndDate = Today.AddDays(-9);
        fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(30), 20);

        var result = await fixture.Service.GetEligibilityAsync(MemberId, 1, null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(28, result.Data!.RemainingQuota);
        Assert.True(result.Data.Eligible);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task RequestedDaysMustFitTheSeparateRemainingCumulativeQuota(int requestedDays, bool expected)
    {
        var fixture = new FreezeFixture();
        fixture.Policies.Policy!.MinFreezeDaysPerRequest = 1;
        fixture.Membership.TotalFrozenDays = 28;

        var result = await fixture.Service.CreateAsync(MemberId, Request(requestedDays), default);

        Assert.Equal(expected, result.IsSuccess);
        Assert.Equal(28, fixture.Membership.TotalFrozenDays);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Pending)]
    [InlineData(FreezeRequestStatus.Approved)]
    [InlineData(FreezeRequestStatus.Active)]
    public async Task InclusiveOverlapWithAnOutstandingRequestIsAConflict(FreezeRequestStatus status)
    {
        var fixture = new FreezeFixture();
        fixture.AddRequest(status, Today.AddDays(5), 3);

        var result = await fixture.Service.CreateAsync(MemberId, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Single(fixture.Membership.MembershipFreezes);
    }

    [Fact]
    public async Task MemberCanEditAnOwnedPendingRequestAndCaptureTheCurrentPolicy()
    {
        var fixture = new FreezeFixture();
        var created = await fixture.Service.CreateAsync(MemberId, Request(), default);
        Assert.True(created.IsSuccess);
        var stored = Assert.Single(fixture.Membership.MembershipFreezes);
        var originalSnapshot = stored.AppliedPolicySnapshot;
        fixture.Policies.Policy!.DefaultMaxFreezeDays = 9;
        var before = MembershipState(fixture.Membership);

        var result = await fixture.Service.UpdateAsync(MemberId, stored.Id,
            Request(4) with { StartDate = Today.AddDays(5), Reason = "Updated travel dates" }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Today.AddDays(5), stored.RequestedStartDate);
        Assert.Equal(Today.AddDays(8), stored.RequestedEndDate);
        Assert.Equal("Updated travel dates", stored.Reason);
        Assert.Equal(FreezeRequestStatus.Pending, stored.RequestStatus);
        Assert.NotEqual(originalSnapshot, stored.AppliedPolicySnapshot);
        Assert.Equal(9, result.Data!.AppliedPolicy!.Policy!.MaximumFreezeDays);
        Assert.Equal(before, MembershipState(fixture.Membership));
        Assert.Single(fixture.Membership.MembershipFreezes);
    }

    [Fact]
    public async Task EditRevalidatesCurrentPolicyAndPreservesTheRequestOnFailure()
    {
        var fixture = new FreezeFixture();
        var stored = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        fixture.Policies.Policy!.MinFreezeDaysPerRequest = 1;
        fixture.Policies.Policy.DefaultMaxFreezeDays = 2;

        var result = await fixture.Service.UpdateAsync(MemberId, stored.Id, Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(Today.AddDays(3), stored.RequestedStartDate);
        Assert.Equal(Today.AddDays(5), stored.RequestedEndDate);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task TrackingAHistoricalRequestPreservesItsSubmittedPolicyAfterAConfigurationChange()
    {
        var fixture = new FreezeFixture();
        var created = await fixture.Service.CreateAsync(MemberId, Request(), default);
        Assert.True(created.IsSuccess);
        var stored = Assert.Single(fixture.Membership.MembershipFreezes);
        stored.RequestStatus = FreezeRequestStatus.Approved;
        stored.ReviewNote = "Approved for the requested period";
        fixture.Policies.Policy!.DefaultMaxFreezeDays = 20;
        fixture.Policies.Policy.MinFreezeDaysPerRequest = 7;

        var read = await fixture.Service.GetAsync(MemberId, stored.Id, default);
        var list = await fixture.Service.ListAsync(MemberId, default);

        Assert.True(read.IsSuccess);
        Assert.True(list.IsSuccess);
        Assert.Equal("Approved", read.Data!.Status);
        Assert.Equal("Original purchased plan", read.Data.PlanName);
        Assert.Equal(stored.ReviewNote, read.Data.ReviewNote);
        Assert.Equal(UtcNow, read.Data.SubmittedAt);
        Assert.Equal(3, read.Data.AppliedPolicy!.Policy!.MinimumFreezeDays);
        Assert.Equal(10, read.Data.AppliedPolicy.Policy.MaximumFreezeDays);
        Assert.False(read.Data.CanEdit);
        Assert.False(read.Data.CanCancel);
        var listed = Assert.Single(list.Data!);
        Assert.Equal(read.Data.FreezeRequestId, listed.FreezeRequestId);
        Assert.Equal(read.Data.Status, listed.Status);
        Assert.Equal(read.Data.AppliedPolicy.Policy, listed.AppliedPolicy!.Policy);
    }

    [Fact]
    public async Task CancelRetainsPendingRequestHistoryAndMembershipState()
    {
        var fixture = new FreezeFixture();
        var stored = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        var before = MembershipState(fixture.Membership);

        var result = await fixture.Service.CancelAsync(MemberId, stored.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Cancelled, stored.RequestStatus);
        Assert.Equal("Cancelled", result.Data!.Status);
        Assert.Same(stored, Assert.Single(fixture.Membership.MembershipFreezes));
        Assert.False(stored.IsDeleted);
        Assert.Equal(before, MembershipState(fixture.Membership));
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Approved)]
    [InlineData(FreezeRequestStatus.Rejected)]
    [InlineData(FreezeRequestStatus.Cancelled)]
    [InlineData(FreezeRequestStatus.Expired)]
    [InlineData(FreezeRequestStatus.Active)]
    [InlineData(FreezeRequestStatus.Completed)]
    public async Task NonPendingRequestsCannotBeEditedOrCancelled(FreezeRequestStatus status)
    {
        var fixture = new FreezeFixture();
        var stored = fixture.AddRequest(status, Today.AddDays(3), 3);

        var edit = await fixture.Service.UpdateAsync(MemberId, stored.Id, Request(), default);
        var cancel = await fixture.Service.CancelAsync(MemberId, stored.Id, default);

        Assert.False(edit.IsSuccess);
        Assert.False(cancel.IsSuccess);
        Assert.Equal(409, edit.StatusCode);
        Assert.Equal(409, cancel.StatusCode);
        Assert.Equal(status, stored.RequestStatus);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task RequestReadEditCancelAndEarlyUnfreezeAreRestrictedToTheOwner()
    {
        var fixture = new FreezeFixture();
        var stored = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);

        var list = await fixture.Service.ListAsync(OtherMemberId, default);
        var read = await fixture.Service.GetAsync(OtherMemberId, stored.Id, default);
        var edit = await fixture.Service.UpdateAsync(OtherMemberId, stored.Id, Request(), default);
        var cancel = await fixture.Service.CancelAsync(OtherMemberId, stored.Id, default);
        var early = await fixture.Service.RequestEarlyUnfreezeAsync(OtherMemberId, stored.Id, default);

        Assert.True(list.IsSuccess);
        Assert.Empty(list.Data!);
        Assert.Equal(404, read.StatusCode);
        Assert.Equal(404, edit.StatusCode);
        Assert.Equal(404, cancel.StatusCode);
        Assert.Equal(404, early.StatusCode);
        Assert.Equal(FreezeRequestStatus.Pending, stored.RequestStatus);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Active)]
    [InlineData(FreezeRequestStatus.Approved)]
    public async Task EarlyUnfreezeSubmitsOnceWithoutExecutingTheFreeze(FreezeRequestStatus status)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.MembershipStatus = MembershipStatus.Frozen;
        fixture.Membership.TotalFrozenDays = 4;
        fixture.Membership.FreezeCount = 1;
        var stored = fixture.AddRequest(status, Today.AddDays(-2), 5);
        var before = MembershipState(fixture.Membership);

        var result = await fixture.Service.RequestEarlyUnfreezeAsync(MemberId, stored.Id, default);
        var saveCount = fixture.Repository.SaveCount;
        var repeated = await fixture.Service.RequestEarlyUnfreezeAsync(MemberId, stored.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(UtcNow.UtcDateTime, stored.EarlyUnfreezeRequestedAt);
        Assert.Equal(FreezeRequestStatus.Pending, stored.EarlyUnfreezeRequestStatus);
        Assert.Equal("Pending", result.Data!.EarlyUnfreezeStatus);
        Assert.Equal(UtcNow, result.Data.EarlyUnfreezeRequestedAt);
        Assert.Equal(result.Data.EarlyUnfreezeRequestedAt, repeated.Data!.EarlyUnfreezeRequestedAt);
        Assert.Equal(saveCount, fixture.Repository.SaveCount);
        Assert.Null(stored.EarlyUnfreezeAt);
        Assert.Null(stored.ActualEndDate);
        Assert.Equal(status, stored.RequestStatus);
        Assert.Equal(before, MembershipState(fixture.Membership));
    }

    [Theory]
    [InlineData(MembershipStatus.Active, FreezeRequestStatus.Active, -2, 5)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Approved, 1, 7)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Active, -3, 4)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Pending, -2, 5)]
    [InlineData(MembershipStatus.Frozen, FreezeRequestStatus.Completed, -2, 5)]
    public async Task EarlyUnfreezeRequiresACurrentFreezeBeforeItsEndDate(
        MembershipStatus membershipStatus, FreezeRequestStatus requestStatus, int startOffset, int duration)
    {
        var fixture = new FreezeFixture();
        fixture.Membership.MembershipStatus = membershipStatus;
        var stored = fixture.AddRequest(requestStatus, Today.AddDays(startOffset), duration);

        var result = await fixture.Service.RequestEarlyUnfreezeAsync(MemberId, stored.Id, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Null(stored.EarlyUnfreezeRequestedAt);
        Assert.Null(stored.EarlyUnfreezeRequestStatus);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AReasonIsRequiredBeforeARequestCanBeSaved(string? reason)
    {
        var fixture = new FreezeFixture();

        var result = await fixture.Service.CreateAsync(MemberId, Request() with { Reason = reason }, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    private static MembershipFreezeRequest Request(int days = 3) => new(1, Today.AddDays(3), days, "Medical leave");

    private static (MembershipStatus Status, DateOnly EndDate, int TotalFrozenDays, int FreezeCount) MembershipState(
        MemberMembership membership) =>
        (membership.MembershipStatus, membership.EndDate, membership.TotalFrozenDays, membership.FreezeCount);

    private static string Snapshot(string durationProperty, int duration, ComponentType type = ComponentType.GYM) =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 17, ["Type"] = type, [durationProperty] = duration }
        });

    private sealed class FreezeFixture
    {
        public MemberMembership Membership { get; } = new()
        {
            Id = 1,
            MemberUserId = MemberId,
            MembershipStatus = MembershipStatus.Active,
            StartDate = Today.AddDays(-14),
            EndDate = Today.AddDays(90),
            PackageComponentId = 17,
            PackageComponent = new() { Id = 17, ComponentType = ComponentType.GYM, DurationDays = 180 },
            OrderItem = new() { PackageId = 4, PackageNameSnapshot = "Original purchased plan", ComponentTermsSnapshot = Snapshot("Days", 180) }
        };

        public RequestRepositoryStub Repository { get; }
        public PolicyRepositoryStub Policies { get; } = new();
        public MembershipFreezeRequestService Service { get; }

        public FreezeFixture()
        {
            Repository = new(Membership);
            Service = new(Repository, Policies, new MembershipFreezeRequestValidator(), new TestClock());
        }

        public MembershipFreeze AddRequest(FreezeRequestStatus status, DateOnly startDate, int duration)
        {
            var request = new MembershipFreeze
            {
                Id = Membership.MembershipFreezes.Count + 1,
                MemberMembershipId = Membership.Id,
                MemberMembership = Membership,
                RequestedByUserId = MemberId,
                RequestedStartDate = startDate,
                RequestedEndDate = startDate.AddDays(duration - 1),
                RequestStatus = status,
                Reason = "Original reason"
            };
            Membership.MembershipFreezes.Add(request);
            return request;
        }
    }

    private sealed class RequestRepositoryStub(MemberMembership membership) : IMembershipFreezeRequestRepository
    {
        public int SaveCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public CancellationToken LastSaveToken { get; private set; }

        public Task<MemberMembership?> GetOwnedMembershipAsync(
            string memberId, int membershipId, bool forUpdate, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(memberId == membership.MemberUserId && membershipId == membership.Id ? membership : null);
        }

        public Task<List<MembershipFreezeReadModel>> ListOwnedRequestsAsync(string memberId, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(membership.MembershipFreezes
                .Where(request => membership.MemberUserId == memberId && request.RequestedByUserId == memberId)
                .Select(ToReadModel).ToList());
        }

        public Task<MembershipFreezeReadModel?> FindOwnedRequestAsync(string memberId, int requestId, CancellationToken ct)
        {
            LastToken = ct;
            var request = membership.MembershipFreezes.SingleOrDefault(request => request.Id == requestId &&
                membership.MemberUserId == memberId && request.RequestedByUserId == memberId);
            return Task.FromResult(request is null ? null : ToReadModel(request));
        }

        private MembershipFreezeReadModel ToReadModel(MembershipFreeze request) =>
            new(request, membership.OrderItem.PackageNameSnapshot, membership.MembershipStatus);

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            var request = Assert.IsType<MembershipFreeze>(entity);
            request.Id = membership.MembershipFreezes.Count + 1;
            request.MemberMembership = membership;
            membership.MembershipFreezes.Add(request);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSaveToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PolicyRepositoryStub : IMembershipFreezePolicyRepository
    {
        public FreezePolicy? Policy { get; set; } = new()
        {
            Id = 8,
            EligibilityPaidDays = 14,
            MinFreezeDaysPerRequest = 3,
            DefaultMaxFreezeDays = 10,
            MinimumRequestDays = 3,
            TierQuotas = [new() { Duration = 180, Unit = FreezeTierUnit.Days, MaximumCumulativeFreezeDays = 30 }]
        };
        public CancellationToken LastToken { get; private set; }

        public Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(Policy);
        }

        public Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
        {
            LastToken = ct;
            return work();
        }
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
