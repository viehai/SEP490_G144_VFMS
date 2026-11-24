using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>UC-70 View Gym Details &amp; Facilities.</summary>
public sealed class GymDetailsTests
{
    private static readonly MembershipFreezePolicyResponse Policy = new(
        new MembershipFreezePolicyConfiguration(1, 7, 30, 3, 90, DateTime.UtcNow),
        [new MembershipFreezeTierQuotaResponse(12, MembershipFreezeTierUnits.Months, 60)]);

    [Fact]
    public async Task ShowsContactsHoursFacilitiesAndFreezeRules()
    {
        var repo = new Repo();
        var result = await new GymDetailsService(repo, new Policies(Result<MembershipFreezePolicyResponse>.Success(Policy))).GetAsync();

        Assert.True(result.IsSuccess);
        var gym = result.Data!;
        Assert.Equal("Victor Fitness", gym.GymName);
        Assert.Equal("0243 1234 567", gym.PhoneNumber);
        Assert.Equal(new TimeOnly(5, 0), gym.OpenTime);
        Assert.False(gym.IsAcceptingMembers);
        var area = Assert.Single(gym.EquipmentAreas);
        Assert.Equal(("Cardio", 4, 3), (area.CategoryName!, area.Total, area.Available));
        Assert.Equal("Lotus", Assert.Single(gym.YogaStudios).StudioName);
        Assert.Equal("Female", Assert.Single(gym.LockerZones).GenderRestriction);
        Assert.Equal((7, 30, 3, 90), (gym.FreezePolicy!.MinimumFreezeDays, gym.FreezePolicy.MaximumFreezeDays,
            gym.FreezePolicy.MinimumRequestDays, gym.FreezePolicy.EligibilityPaidDays));
        Assert.Equal(60, Assert.Single(gym.FreezePolicy.Quotas).MaximumCumulativeFreezeDays);
        Assert.Equal(1, repo.AskedGymId);
    }

    [Fact]
    public async Task NoGymIsNotFound()
    {
        var result = await new GymDetailsService(new Repo { Gym = null },
            new Policies(Result<MembershipFreezePolicyResponse>.Success(Policy))).GetAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("GYM_NOT_CONFIGURED", result.ErrorCode);
    }

    [Fact]
    public async Task FreezeRulesAreLeftOutWhenNotSetOrUnreadable()
    {
        var notSet = await new GymDetailsService(new Repo(),
            new Policies(Result<MembershipFreezePolicyResponse>.Success(new MembershipFreezePolicyResponse(null, [])))).GetAsync();
        var failed = await new GymDetailsService(new Repo(),
            new Policies(Result<MembershipFreezePolicyResponse>.Failure("down"))).GetAsync();

        Assert.True(notSet.IsSuccess);
        Assert.Null(notSet.Data!.FreezePolicy);
        Assert.True(failed.IsSuccess);
        Assert.Null(failed.Data!.FreezePolicy);
    }

    private sealed class Repo : IGymDetailsRepository
    {
        public Gym? Gym { get; init; } = new()
        {
            Id = 1, GymName = "Victor Fitness", Address = "1 Le Loi", PhoneNumber = "0243 1234 567",
            OpenTime = new TimeOnly(5, 0), CloseTime = new TimeOnly(22, 0), IsAcceptingMembers = false,
        };

        public int? AskedGymId { get; private set; }

        public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) => Task.FromResult(Gym);

        public Task<IReadOnlyList<GymEquipmentArea>> GetEquipmentAreasAsync(int gymId, CancellationToken cancellationToken = default)
        {
            AskedGymId = gymId;
            return Task.FromResult<IReadOnlyList<GymEquipmentArea>>([new("Cardio", "Treadmills and bikes", 4, 3)]);
        }

        public Task<IReadOnlyList<YogaStudio>> GetActiveStudiosAsync(int gymId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaStudio>>([new YogaStudio { StudioName = "Lotus", FloorNumber = 2, Capacity = 20 }]);

        public Task<IReadOnlyList<LockerZone>> GetLockerZonesAsync(int gymId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LockerZone>>(
                [new LockerZone { ZoneName = "A", FloorNumber = 1, GenderRestriction = GenderRestriction.Female, TotalLockers = 40 }]);
    }

    private sealed class Policies(Result<MembershipFreezePolicyResponse> result) : IMembershipFreezePolicyService
    {
        public Task<Result<MembershipFreezePolicyResponse>> GetAsync(CancellationToken ct = default) => Task.FromResult(result);

        public Task<Result<MembershipFreezePolicyResponse>> UpdateAsync(
            MembershipFreezePolicyRequest request, string managerUserId, string? ipAddress, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
