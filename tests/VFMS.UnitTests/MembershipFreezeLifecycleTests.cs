using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipFreezeLifecycleTests
{
    private const string MemberId = "member";
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);

    [Theory]
    [InlineData(-2, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ApprovedFreezeStartsOnTheGymDateOrAfterAMissedStartWithoutReapplyingEntitlements(
        int startOffset, bool expectedTransition)
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(startOffset));
        var before = Entitlements(fixture);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedTransition, result.Data);
        Assert.Equal(expectedTransition ? FreezeRequestStatus.Active : FreezeRequestStatus.Approved,
            fixture.Request.RequestStatus);
        Assert.Equal(expectedTransition ? MembershipStatus.Frozen : MembershipStatus.Active,
            fixture.Membership.MembershipStatus);
        Assert.Equal(expectedTransition ? fixture.Request.RequestedStartDate : (DateOnly?)null,
            fixture.Request.ActualStartDate);
        Assert.Equal(before, Entitlements(fixture));
        Assert.Equal(cancellation.Token, fixture.Repository.LastToken);
        if (expectedTransition)
        {
            Assert.Equal(cancellation.Token, fixture.Repository.LastSaveToken);
            AssertLifecycleNotification(fixture);
        }
        else
        {
            Assert.Empty(fixture.Repository.Notifications);
        }
    }

    [Fact]
    public async Task ImmediateManagerApprovalCanEnterTheActiveFreezeLifecycleOnce()
    {
        using var fixture = new LifecycleFixture();
        fixture.Membership.MembershipStatus = MembershipStatus.Frozen;
        fixture.Request.ActualStartDate = Today;
        var before = Entitlements(fixture);

        var started = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        var repeated = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(started.IsSuccess);
        Assert.True(started.Data);
        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Data);
        Assert.Equal(FreezeRequestStatus.Active, fixture.Request.RequestStatus);
        Assert.Equal(Today, fixture.Request.ActualStartDate);
        Assert.Equal(before, Entitlements(fixture));
        AssertLifecycleNotification(fixture);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public async Task InclusiveFreezeEndRemainsFrozenThroughItsEndDateAndCompletesTheFollowingDay(
        int endOffset, bool expectedTransition)
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(endOffset - 6));
        fixture.MakeActive();
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedTransition, result.Data);
        Assert.Equal(expectedTransition ? FreezeRequestStatus.Completed : FreezeRequestStatus.Active,
            fixture.Request.RequestStatus);
        Assert.Equal(expectedTransition ? MembershipStatus.Active : MembershipStatus.Frozen,
            fixture.Membership.MembershipStatus);
        Assert.Equal(expectedTransition ? fixture.Request.RequestedEndDate : (DateOnly?)null,
            fixture.Request.ActualEndDate);
        Assert.Equal(before, Entitlements(fixture));
        if (expectedTransition)
            AssertLifecycleNotification(fixture);
        else
            Assert.Empty(fixture.Repository.Notifications);
    }

    [Fact]
    public async Task EntirelyMissedApprovedPeriodCompletesWithoutGrantingAnotherExtension()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        var repeated = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(fixture.Request.RequestedStartDate, fixture.Request.ActualStartDate);
        Assert.Equal(fixture.Request.RequestedEndDate, fixture.Request.ActualEndDate);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        AssertLifecycleNotification(fixture);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task PendingRequestExpiresOnlyAfterItsStartDateWithoutEntitlementChanges(
        int startOffset, bool expectedTransition)
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(startOffset));
        fixture.Request.RequestStatus = FreezeRequestStatus.Pending;
        fixture.Request.AppliedExpiryExtensionDays = null;
        fixture.Request.AppliedPolicySnapshot = null;
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        var repeated = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedTransition, result.Data);
        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Data);
        Assert.Equal(expectedTransition ? FreezeRequestStatus.Expired : FreezeRequestStatus.Pending,
            fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Null(fixture.Request.ActualStartDate);
        Assert.Null(fixture.Request.ActualEndDate);
        Assert.Equal(before, Entitlements(fixture));
        if (expectedTransition)
            AssertLifecycleNotification(fixture);
        else
            Assert.Empty(fixture.Repository.Notifications);
    }

    [Fact]
    public async Task CompletionRestoresExpiredWhenTheMembershipTermHasAlreadyEnded()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.MakeActive();
        fixture.Membership.EndDate = Today.AddDays(-1);
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Equal(MembershipStatus.Expired, fixture.Membership.MembershipStatus);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(before, Entitlements(fixture));
    }

    [Fact]
    public async Task EntirelyMissedApprovedPeriodCompletesAfterMembershipExpiryWithoutRestoringAccess()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-10));
        fixture.Membership.MembershipStatus = MembershipStatus.Expired;
        fixture.Membership.EndDate = Today.AddDays(-1);
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(Today.AddDays(-10), fixture.Request.ActualStartDate);
        Assert.Equal(Today.AddDays(-4), fixture.Request.ActualEndDate);
        Assert.Equal(MembershipStatus.Expired, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        AssertLifecycleNotification(fixture);
    }

    [Fact]
    public async Task CompletionPreservesAnIndependentSuspension()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.MakeActive();
        fixture.Membership.MembershipStatus = MembershipStatus.Suspended;

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Suspended, fixture.Membership.MembershipStatus);
    }

    [Fact]
    public async Task CompletionDoesNotRestoreAccessOverAnotherCurrentEffectiveFreeze()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.MakeActive();
        fixture.Membership.MembershipFreezes.Add(new()
        {
            Id = 2,
            MemberMembershipId = fixture.Membership.Id,
            RequestedByUserId = MemberId,
            RequestStatus = FreezeRequestStatus.Active,
            RequestedStartDate = Today,
            ActualStartDate = Today,
            RequestedEndDate = Today.AddDays(6),
            AppliedExpiryExtensionDays = 7,
            AppliedPolicySnapshot = ValidPolicySnapshot()
        });

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Frozen, fixture.Membership.MembershipStatus);
    }

    [Fact]
    public async Task AdjacentApprovedFreezeKeepsAccessFrozenBetweenLifecycleTransactions()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.MakeActive();
        fixture.Membership.TotalFrozenDays = 14;
        fixture.Membership.FreezeCount = 2;
        fixture.Membership.EndDate = fixture.Membership.EndDate.AddDays(7);
        var next = new MembershipFreeze
        {
            Id = 2,
            MemberMembershipId = fixture.Membership.Id,
            RequestedByUserId = MemberId,
            RequestStatus = FreezeRequestStatus.Approved,
            RequestedStartDate = Today,
            RequestedEndDate = Today.AddDays(6),
            AppliedExpiryExtensionDays = 7,
            AppliedPolicySnapshot = ValidPolicySnapshot()
        };
        fixture.Membership.MembershipFreezes.Add(next);
        var before = Entitlements(fixture);

        var completed = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(completed.IsSuccess);
        Assert.True(completed.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(FreezeRequestStatus.Approved, next.RequestStatus);
        Assert.Null(next.ActualStartDate);
        Assert.Equal(MembershipStatus.Frozen, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));

        var started = await fixture.Service.ProcessAsync(next.Id, default);

        Assert.True(started.IsSuccess);
        Assert.True(started.Data);
        Assert.Equal(FreezeRequestStatus.Active, next.RequestStatus);
        Assert.Equal(Today, next.ActualStartDate);
        Assert.Equal(MembershipStatus.Frozen, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        Assert.Equal(2, fixture.Repository.Notifications.Count);
        Assert.Equal(2, fixture.Repository.CompletedSaves);
    }

    [Fact]
    public async Task NaturalCompletionExpiresAnUnresolvedEarlyUnfreezeRequest()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.MakeActive();
        fixture.Request.EarlyUnfreezeRequestedAt = UtcNow.AddDays(-2).UtcDateTime;
        fixture.Request.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Pending;

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(FreezeRequestStatus.Expired, fixture.Request.EarlyUnfreezeRequestStatus);
        Assert.Null(fixture.Request.EarlyUnfreezeAt);
        Assert.Null(fixture.Request.EarlyUnfreezeReviewedByUserId);
    }

    [Fact]
    public async Task ApprovedEarlyUnfreezeIsTerminalEvenAfterTheOriginalPeriodEnds()
    {
        using var fixture = new LifecycleFixture();
        fixture.SetPeriod(Today.AddDays(-7));
        fixture.Request.RequestStatus = FreezeRequestStatus.Completed;
        fixture.Request.ActualStartDate = fixture.Request.RequestedStartDate;
        fixture.Request.ActualEndDate = fixture.Request.RequestedStartDate.AddDays(2);
        fixture.Request.EarlyUnfreezeAt = UtcNow.AddDays(-4).UtcDateTime;
        fixture.Request.EarlyUnfreezeRequestStatus = FreezeRequestStatus.Approved;
        fixture.Request.AppliedExpiryExtensionDays = 3;
        fixture.Membership.TotalFrozenDays = 3;
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.EarlyUnfreezeRequestStatus);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        Assert.Empty(fixture.Repository.Notifications);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public async Task IncompleteHistoricalApprovalFailsSafelyAndTheNextValidRecordStillProcesses(
        int? appliedExtension, bool hasSnapshot)
    {
        using var incomplete = new LifecycleFixture();
        incomplete.Request.AppliedExpiryExtensionDays = appliedExtension;
        if (!hasSnapshot)
            incomplete.Request.AppliedPolicySnapshot = null;
        var before = Entitlements(incomplete);
        using var valid = new LifecycleFixture();

        var failed = await incomplete.Service.ProcessAsync(incomplete.Request.Id, default);
        var processed = await valid.Service.ProcessAsync(valid.Request.Id, default);

        Assert.False(failed.IsSuccess);
        Assert.Equal(409, failed.StatusCode);
        Assert.Equal(FreezeRequestStatus.Approved, incomplete.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Active, incomplete.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(incomplete));
        Assert.Empty(incomplete.Repository.Notifications);
        Assert.True(processed.IsSuccess);
        Assert.True(processed.Data);
        Assert.Equal(FreezeRequestStatus.Active, valid.Request.RequestStatus);
    }

    [Theory]
    [InlineData(2026, 11, 30)]
    [InlineData(2026, 12, 31)]
    public async Task LifecycleUsesInclusiveGymDatesAcrossMonthAndYearBoundaries(int year, int month, int day)
    {
        using var fixture = new LifecycleFixture();
        var start = new DateOnly(year, month, day);
        fixture.SetPeriod(start);
        fixture.Clock.Now = GymMidnight(start);
        var before = Entitlements(fixture);

        var started = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        fixture.Clock.Now = GymMidnight(start.AddDays(6));
        var onEndDate = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        fixture.Clock.Now = GymMidnight(start.AddDays(7));
        var completed = await fixture.Service.ProcessAsync(fixture.Request.Id, default);
        var repeated = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.True(started.Data);
        Assert.False(onEndDate.Data);
        Assert.True(completed.Data);
        Assert.False(repeated.Data);
        Assert.Equal(start, fixture.Request.ActualStartDate);
        Assert.Equal(start.AddDays(6), fixture.Request.ActualEndDate);
        Assert.Equal(FreezeRequestStatus.Completed, fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        Assert.Equal(2, fixture.Repository.Notifications.Count);
        Assert.Equal(2, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task LifecycleRechecksOwnershipBeforeMutatingTheParentMembership()
    {
        using var fixture = new LifecycleFixture();
        fixture.Request.RequestedByUserId = "another-member";
        var before = Entitlements(fixture);

        var result = await fixture.Service.ProcessAsync(fixture.Request.Id, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(FreezeRequestStatus.Approved, fixture.Request.RequestStatus);
        Assert.Equal(before, Entitlements(fixture));
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task ConcurrentLifecycleExecutionsCommitOnlyOneStartNotification()
    {
        using var fixture = new LifecycleFixture();
        var before = Entitlements(fixture);

        var results = await Task.WhenAll(fixture.Service.ProcessAsync(fixture.Request.Id, default),
            fixture.Service.ProcessAsync(fixture.Request.Id, default));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Where(result => result.Data));
        Assert.Single(results.Where(result => !result.Data));
        Assert.Equal(FreezeRequestStatus.Active, fixture.Request.RequestStatus);
        Assert.Equal(MembershipStatus.Frozen, fixture.Membership.MembershipStatus);
        Assert.Equal(before, Entitlements(fixture));
        AssertLifecycleNotification(fixture);
    }

    [Fact]
    public async Task CancellationIsPropagatedInsteadOfBeingReportedAsALegacyRecord()
    {
        using var fixture = new LifecycleFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ProcessAsync(fixture.Request.Id, cancellation.Token));

        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Repository.Notifications);
    }

    private static void AssertLifecycleNotification(LifecycleFixture fixture)
    {
        var notification = Assert.Single(fixture.Repository.Notifications);
        Assert.Equal(MemberId, notification.RecipientUserId);
        Assert.Equal(nameof(MembershipFreeze), notification.EntityType);
        Assert.Equal(fixture.Request.Id, notification.EntityId);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Equal(1, fixture.Repository.CompletedSaves);
    }

    private static (DateOnly Expiry, int FrozenDays, int FreezeCount, int? AppliedExtension) Entitlements(
        LifecycleFixture fixture) =>
        (fixture.Membership.EndDate, fixture.Membership.TotalFrozenDays, fixture.Membership.FreezeCount,
            fixture.Request.AppliedExpiryExtensionDays);

    private static DateTimeOffset GymMidnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));

    private static string ValidPolicySnapshot() => JsonSerializer.Serialize(new MembershipFreezePolicyResponse(
        new(8, 3, 10, 3, 14, UtcNow.AddDays(-10).UtcDateTime),
        [new(180, MembershipFreezeTierUnits.Days, 30)]));

    private sealed class LifecycleFixture : IDisposable
    {
        public MemberMembership Membership { get; } = new()
        {
            Id = 1,
            MemberUserId = MemberId,
            MembershipStatus = MembershipStatus.Active,
            StartDate = Today.AddDays(-30),
            EndDate = Today.AddDays(97),
            TotalFrozenDays = 7,
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
        public TestClock Clock { get; } = new();
        public TransactionRepositoryStub Transactions { get; } = new();
        public LifecycleRepositoryStub Repository { get; }
        public MembershipFreezeLifecycleService Service { get; }

        public LifecycleFixture()
        {
            Request = new()
            {
                Id = 1,
                MemberMembershipId = Membership.Id,
                MemberMembership = Membership,
                RequestedByUserId = MemberId,
                RequestedStartDate = Today,
                RequestedEndDate = Today.AddDays(6),
                RequestStatus = FreezeRequestStatus.Approved,
                AppliedExpiryExtensionDays = 7,
                AppliedPolicySnapshot = ValidPolicySnapshot(),
                ReviewedByUserId = "manager",
                ReviewedAt = UtcNow.AddDays(-4).UtcDateTime
            };
            Membership.MembershipFreezes.Add(Request);
            Repository = new(Membership, Transactions);
            Service = new(Repository, new RequestRepositoryStub(Membership, Transactions), Transactions, Clock);
        }

        public void SetPeriod(DateOnly start)
        {
            Request.RequestedStartDate = start;
            Request.RequestedEndDate = start.AddDays(6);
        }

        public void MakeActive()
        {
            Request.RequestStatus = FreezeRequestStatus.Active;
            Request.ActualStartDate = Request.RequestedStartDate;
            Membership.MembershipStatus = MembershipStatus.Frozen;
        }

        public void Dispose() => Transactions.Dispose();
    }

    private sealed class LifecycleRepositoryStub(MemberMembership membership, TransactionRepositoryStub transactions)
        : IMembershipFreezeLifecycleRepository
    {
        public List<Notification> Notifications { get; } = [];
        public int SaveCount { get; private set; }
        public int CompletedSaves { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public CancellationToken LastSaveToken { get; private set; }

        public Task<MembershipFreezeLifecycleTarget?> FindAsync(int requestId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastToken = ct;
            var request = membership.MembershipFreezes.SingleOrDefault(request => request.Id == requestId);
            return Task.FromResult(request is null ? null :
                new MembershipFreezeLifecycleTarget(request.Id, request.MemberMembershipId, membership.MemberUserId));
        }

        public Task<IReadOnlyList<int>> ListDueAsync(DateOnly today, int afterId, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            Assert.True(transactions.TransactionOpen);
            Notifications.Add(Assert.IsType<Notification>(entity));
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(transactions.TransactionOpen);
            LastSaveToken = cancellationToken;
            SaveCount++;
            await Task.Yield();
            CompletedSaves++;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RequestRepositoryStub(MemberMembership membership, TransactionRepositoryStub transactions)
        : IMembershipFreezeRequestRepository
    {
        public Task<MemberMembership?> GetOwnedMembershipAsync(
            string memberId, int membershipId, bool forUpdate, CancellationToken ct)
        {
            Assert.True(forUpdate);
            Assert.True(transactions.TransactionOpen);
            return Task.FromResult(memberId == membership.MemberUserId && membershipId == membership.Id ? membership : null);
        }
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

    private sealed class TransactionRepositoryStub : IMembershipFreezePolicyRepository, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        public bool TransactionOpen { get; private set; }

        public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            TransactionOpen = true;
            try
            {
                await Task.Yield();
                await work();
            }
            finally
            {
                TransactionOpen = false;
                _gate.Release();
            }
        }

        public Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct) =>
            throw new NotSupportedException("Lifecycle execution must preserve the approved policy.");
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() => _gate.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
