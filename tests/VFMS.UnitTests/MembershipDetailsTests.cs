using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipDetailsTests
{
    private const string MemberId = "member";
    private const string OtherMemberId = "other-member";
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ListReturnsAllAndOnlyOwnedMemberships(int ownedCount)
    {
        var repository = new RepositoryStub();
        for (var id = 1; id <= ownedCount; id++)
            repository.Add(MemberId, Membership(id, MembershipStatus.Active));
        repository.Add(OtherMemberId, Membership(42, MembershipStatus.Active));
        using var cancellation = new CancellationTokenSource();

        var result = await CreateService(repository).ListOwnedMembershipsAsync(MemberId, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(Enumerable.Range(1, ownedCount), result.Data.Select(membership => membership.MembershipId).Order());
        Assert.Equal((MemberId, (int?)null, cancellation.Token), repository.LastQuery);
    }

    [Theory]
    [InlineData(MembershipStatus.Scheduled, 30, "Scheduled")]
    [InlineData(MembershipStatus.Active, 0, "Active")]
    [InlineData(MembershipStatus.Active, -1, "Expired")]
    [InlineData(MembershipStatus.Frozen, -1, "Frozen")]
    [InlineData(MembershipStatus.Expired, -1, "Expired")]
    [InlineData(MembershipStatus.Cancelled, 2, "Cancelled")]
    [InlineData(MembershipStatus.Suspended, 2, "Suspended")]
    public async Task DetailReturnsOwnedPlanAndDatesWithCanonicalStatus(
        MembershipStatus status, int expiryOffset, string expectedStatus)
    {
        var membership = Membership(1, status) with
        {
            StartDate = status == MembershipStatus.Scheduled ? Today.AddDays(1) : Today.AddDays(-29),
            EndDate = Today.AddDays(expiryOffset)
        };
        var repository = new RepositoryStub();
        repository.Add(MemberId, membership);
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();

        var list = await service.ListOwnedMembershipsAsync(MemberId, cancellation.Token);
        var result = await service.GetOwnedMembershipAsync(MemberId, membership.MembershipId, cancellation.Token);

        Assert.True(result.IsSuccess);
        var detail = Assert.IsType<MembershipDetailResponse>(result.Data);
        Assert.Equal(new MembershipSummaryResponse(1, 17, "Gym 30 days at purchase", "GYM",
            membership.StartDate, membership.EndDate, expectedStatus), detail.Membership);
        Assert.True(list.IsSuccess);
        Assert.Equal(detail.Membership, Assert.Single(list.Data!));
        Assert.Empty(detail.Freeze.History);
        Assert.Equal((MemberId, (int?)membership.MembershipId, cancellation.Token), repository.LastQuery);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(99)]
    public async Task OtherMembersAndMissingMembershipsReturnNotFound(int membershipId)
    {
        var repository = new RepositoryStub();
        repository.Add(MemberId, Membership(1, MembershipStatus.Active));
        repository.Add(OtherMemberId, Membership(42, MembershipStatus.Active));

        var result = await CreateService(repository).GetOwnedMembershipAsync(MemberId, membershipId, default);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("MEMBERSHIP_NOT_FOUND", result.ErrorCode);
        Assert.Equal((MemberId, (int?)membershipId, CancellationToken.None), repository.LastQuery);
    }

    [Fact]
    public async Task DetailMapsPersistedFreezeUsageAndHistory()
    {
        var current = new OwnedMembershipFreeze(Today.AddDays(-2), Today.AddDays(2), Today.AddDays(-2), null,
            FreezeRequestStatus.Active);
        var completed = new OwnedMembershipFreeze(Today.AddDays(-10), Today.AddDays(-5), Today.AddDays(-10),
            Today.AddDays(-7), FreezeRequestStatus.Completed);
        var membership = Membership(1, MembershipStatus.Frozen);
        var repository = new RepositoryStub();
        repository.Add(MemberId, new OwnedMembershipDetail(
            membership, DateTime.SpecifyKind(UtcNow.UtcDateTime, DateTimeKind.Unspecified), "INV-123", 4, 1, [current, completed]));

        var result = await CreateService(repository).GetOwnedMembershipAsync(MemberId, membership.MembershipId, default);

        Assert.True(result.IsSuccess);
        var detail = Assert.IsType<MembershipDetailResponse>(result.Data);
        Assert.Equal<DateTimeOffset?>(UtcNow, detail.PurchasedAt);
        Assert.Equal("INV-123", detail.InvoiceNumber);
        Assert.Equal(4, detail.Freeze.TotalFrozenDays);
        Assert.Equal(1, detail.Freeze.FreezeCount);
        Assert.Collection(detail.Freeze.History,
            freeze => Assert.Equal(new MembershipFreezePeriodResponse(current.RequestedStartDate,
                current.RequestedEndDate, current.ActualStartDate, null, "Active"), freeze),
            freeze => Assert.Equal(new MembershipFreezePeriodResponse(completed.RequestedStartDate,
                completed.RequestedEndDate, completed.ActualStartDate, completed.ActualEndDate, "Completed"), freeze));
    }

    private static MembershipPurchaseService CreateService(RepositoryStub repository) =>
        new(repository, new UnusedPaymentGateway(), new TestClock());

    private static OwnedMembership Membership(int id, MembershipStatus status) =>
        new(id, 17, "Gym 30 days at purchase", ComponentType.GYM, Today.AddDays(-15), Today.AddDays(14), status);

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RepositoryStub : IMembershipPurchaseRepository
    {
        private readonly List<(string OwnerId, OwnedMembershipDetail Detail)> _memberships = [];

        public (string? MemberId, int? MembershipId, CancellationToken Token) LastQuery { get; private set; }

        public void Add(string ownerId, OwnedMembership membership) =>
            Add(ownerId, new OwnedMembershipDetail(membership, null, null, 0, 0, []));

        public void Add(string ownerId, OwnedMembershipDetail detail) => _memberships.Add((ownerId, detail));

        public Task<List<OwnedMembership>> ListOwnedMembershipsAsync(string memberId, CancellationToken ct)
        {
            LastQuery = (memberId, null, ct);
            return Task.FromResult(_memberships.Where(row => row.OwnerId == memberId)
                .Select(row => row.Detail.Membership).ToList());
        }

        public Task<OwnedMembershipDetail?> FindOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct)
        {
            LastQuery = (memberId, membershipId, ct);
            return Task.FromResult(_memberships.Where(row => row.OwnerId == memberId)
                .Select(row => row.Detail).SingleOrDefault(detail => detail.Membership.MembershipId == membershipId));
        }

        public Task<T> ExecuteInSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<List<Package>> LoadPlansAsync(int[] ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<ApplicationUser>> LoadCheckoutUsersAsync(string purchaserId, string[] emails, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<List<ApplicationUser>> LockUsersAsync(string[] userIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplicationRole?> GetMemberRoleAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct) => throw new NotSupportedException();
        public Task<Voucher?> FindVoucherForUpdateAsync(string normalizedCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<(int Confirmed, int Reserved)> CountVoucherCapacityAsync(int voucherId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Gym?> GetRenewalPolicyGymAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<List<MemberMembership>> LoadRenewableMembershipsAsync(string memberId, int? membershipId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<List<Order>> LoadRenewalOrdersAsync(int[] membershipIds, bool forUpdate, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string?> FindRenewalPurchaserAsync(string orderNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<MemberMembership>> LoadDueMembershipsAsync(DateOnly today, CancellationToken ct) =>
            throw new NotSupportedException();
        public void Add(Order order) => throw new NotSupportedException();
        public void Add(AuditLog audit) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnusedPaymentGateway : IMembershipPaymentGateway
    {
        public VietQrPaymentResponse? CreateVietQr(string orderNumber, decimal amount) => throw new NotSupportedException();
        public Result<SepayTransfer> Verify(string authorization, string timestamp, string signature, byte[] rawBody, DateTimeOffset now) =>
            throw new NotSupportedException();
    }
}
