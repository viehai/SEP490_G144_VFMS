using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipAccessTests
{
    private const string MemberId = "member";
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);

    [Theory]
    [InlineData(MembershipStatus.Active, true)]
    [InlineData(MembershipStatus.Frozen, false)]
    [InlineData(MembershipStatus.Scheduled, false)]
    [InlineData(MembershipStatus.Expired, false)]
    [InlineData(MembershipStatus.Cancelled, false)]
    [InlineData(MembershipStatus.Suspended, false)]
    public async Task GymEntryAndClassBookingRequireTheCanonicalActiveStatus(MembershipStatus status, bool expected)
    {
        var repository = new AccessRepositoryStub { Membership = Membership(ComponentType.GYM) with { Status = status } };
        var service = new MembershipAccessService(repository, new TestClock());

        var gym = await service.CheckGymEntryAsync(MemberId, 1, default);
        repository.Membership = repository.Membership with { ComponentType = ComponentType.YOGA };
        var yoga = await service.CheckClassBookingAsync(MemberId, 1, default);

        Assert.Equal(expected, gym.IsSuccess);
        Assert.Equal(expected, yoga.IsSuccess);
        if (expected)
        {
            Assert.True(gym.Data);
            Assert.True(yoga.Data);
        }
        else
        {
            Assert.Equal(403, gym.StatusCode);
            Assert.Equal("MSG-36", gym.ErrorCode);
            Assert.Equal(403, yoga.StatusCode);
            Assert.Equal("MSG-31", yoga.ErrorCode);
        }
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(-1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(-2, -1, false)]
    public async Task AccessUsesInclusiveActivationAndExpiryOnTheGymCalendar(
        int startOffset, int endOffset, bool expected)
    {
        var repository = new AccessRepositoryStub
        {
            Membership = Membership(ComponentType.GYM) with
            {
                StartDate = Today.AddDays(startOffset),
                EndDate = Today.AddDays(endOffset)
            }
        };
        var service = new MembershipAccessService(repository, new TestClock());

        var gym = await service.CheckGymEntryAsync(MemberId, 1, default);
        repository.Membership = repository.Membership with { ComponentType = ComponentType.YOGA };
        var yoga = await service.CheckClassBookingAsync(MemberId, 1, default);

        Assert.Equal(expected, gym.IsSuccess);
        Assert.Equal(expected, yoga.IsSuccess);
    }

    [Theory]
    [InlineData(ComponentType.GYM, true)]
    [InlineData(ComponentType.YOGA, false)]
    [InlineData(ComponentType.PT, false)]
    [InlineData(ComponentType.PT, true)]
    public async Task AccessDoesNotUseAnEntitlementForAnotherService(ComponentType component, bool classBooking)
    {
        var repository = new AccessRepositoryStub { Membership = Membership(component) };
        var service = new MembershipAccessService(repository, new TestClock());

        var result = classBooking
            ? await service.CheckClassBookingAsync(MemberId, 1, default)
            : await service.CheckGymEntryAsync(MemberId, 1, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(classBooking ? "MSG-31" : "MSG-36", result.ErrorCode);
    }

    [Theory]
    [InlineData("another-member", 1)]
    [InlineData(MemberId, 999)]
    public async Task AccessCannotUseAnotherMembersOrAMissingEntitlement(string memberId, int membershipId)
    {
        var repository = new AccessRepositoryStub { Membership = Membership(ComponentType.GYM) };
        var service = new MembershipAccessService(repository, new TestClock());
        using var cancellation = new CancellationTokenSource();

        var gym = await service.CheckGymEntryAsync(memberId, membershipId, cancellation.Token);
        var yoga = await service.CheckClassBookingAsync(memberId, membershipId, cancellation.Token);

        Assert.Equal(404, gym.StatusCode);
        Assert.Equal(404, yoga.StatusCode);
        Assert.Equal("MEMBERSHIP_NOT_FOUND", gym.ErrorCode);
        Assert.Equal("MEMBERSHIP_NOT_FOUND", yoga.ErrorCode);
        Assert.Equal(cancellation.Token, repository.LastToken);
    }

    [Fact]
    public async Task CanonicalFreezeAndEarlyUnfreezeStateChangesControlAccess()
    {
        var repository = new AccessRepositoryStub { Membership = Membership(ComponentType.GYM) };
        var service = new MembershipAccessService(repository, new TestClock());

        var active = await service.CheckGymEntryAsync(MemberId, 1, default);
        repository.Membership = repository.Membership with { Status = MembershipStatus.Frozen };
        var frozen = await service.CheckGymEntryAsync(MemberId, 1, default);
        repository.Membership = repository.Membership with { Status = MembershipStatus.Active };
        var restored = await service.CheckGymEntryAsync(MemberId, 1, default);

        Assert.True(active.IsSuccess);
        Assert.False(frozen.IsSuccess);
        Assert.Equal("MSG-36", frozen.ErrorCode);
        Assert.True(restored.IsSuccess);
    }

    [Fact]
    public async Task AccessPropagatesCancellationToItsOwnershipQuery()
    {
        var repository = new AccessRepositoryStub { Membership = Membership(ComponentType.GYM) };
        var service = new MembershipAccessService(repository, new TestClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckGymEntryAsync(MemberId, 1, cancellation.Token));
    }

    private static OwnedMembershipAccess Membership(ComponentType component) =>
        new(1, component, MembershipStatus.Active, Today.AddDays(-10), Today.AddDays(30));

    private sealed class AccessRepositoryStub : IMembershipAccessRepository
    {
        public required OwnedMembershipAccess Membership { get; set; }
        public CancellationToken LastToken { get; private set; }

        public Task<OwnedMembershipAccess?> FindOwnedAsync(string memberId, int membershipId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastToken = ct;
            return Task.FromResult(memberId == MemberId && membershipId == Membership.MembershipId ? Membership : null);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
