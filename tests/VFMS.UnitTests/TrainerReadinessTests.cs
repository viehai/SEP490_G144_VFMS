using System.Reflection;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;

namespace VFMS.UnitTests;

public class TrainerReadinessTests
{
    [Theory]
    [InlineData(0, 59)]
    [InlineData(9, 59)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    public void OnlyTenContinuousAcceptingDaysClearAccumulatedInactivity(int acceptingDays, int expected)
    {
        var day = new DateOnly(2026, 11, 23);
        var profile = new TrainerProfile
        {
            AcceptingNewMembers = false, NotAcceptingSince = day.AddDays(-59),
            NotAcceptingCountFrom = day.AddDays(-59)
        };
        TrainerReadinessRules.SetAccepting(profile, true, day, false);
        TrainerReadinessRules.SetAccepting(profile, false, day.AddDays(acceptingDays), false);

        Assert.Equal(expected, profile.NotAcceptingAccumulatedDays);
        TrainerReadinessRules.AccumulateInactiveDays(profile, day.AddDays(acceptingDays + 1), false);
        Assert.Equal(expected + 1, profile.NotAcceptingAccumulatedDays);
        Assert.False(TrainerReadinessRules.HasExpired(profile));
        TrainerReadinessRules.AccumulateInactiveDays(profile, day.AddDays(acceptingDays + 2), false);
        Assert.Equal(expected == 59, TrainerReadinessRules.HasExpired(profile));
    }

    [Fact]
    public void RepeatedBriefAcceptingPeriodsPauseAndResumeTheSameCounter()
    {
        var day = new DateOnly(2026, 11, 23);
        var profile = new TrainerProfile
        {
            AcceptingNewMembers = false, NotAcceptingSince = day.AddDays(-59),
            NotAcceptingCountFrom = day.AddDays(-59)
        };
        TrainerReadinessRules.SetAccepting(profile, true, day, false);
        TrainerReadinessRules.SetAccepting(profile, false, day.AddDays(9), false);
        TrainerReadinessRules.SetAccepting(profile, true, day.AddDays(10), false);
        TrainerReadinessRules.SetAccepting(profile, false, day.AddDays(19), false);
        Assert.Equal(60, profile.NotAcceptingAccumulatedDays);
        TrainerReadinessRules.AccumulateInactiveDays(profile, day.AddDays(20), false);
        Assert.True(TrainerReadinessRules.HasExpired(profile));
    }

    [Fact]
    public void RepeatingAcceptingRequestDoesNotRestartTheTenDayWindow()
    {
        var day = new DateOnly(2026, 11, 23);
        var profile = new TrainerProfile { NotAcceptingSince = day.AddDays(-59) };
        TrainerReadinessRules.SetAccepting(profile, true, day, false);
        TrainerReadinessRules.SetAccepting(profile, true, day.AddDays(9), false);
        TrainerReadinessRules.SetAccepting(profile, false, day.AddDays(10), false);
        Assert.Equal(0, profile.NotAcceptingAccumulatedDays);
    }

    [Fact]
    public async Task SwitchingToAcceptingDoesNotCountDaysProtectedByRunningContract()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var profile = new TrainerProfile
        {
            PTUserId = "trainer", IsActive = true, AcceptingNewMembers = false,
            NotAcceptingSince = today.AddDays(-90), NotAcceptingAccumulatedDays = 7
        };
        var trainerRepo = ReadinessRepository.Create<ITrainerRepository>(profile);
        ((ReadinessRepository)(object)trainerRepo).Running = true;
        var trainer = new TrainerService(trainerRepo, null!, null!);

        Assert.True((await trainer.SetReadiness(true, "trainer", default)).IsSuccess);
        Assert.Equal(7, profile.NotAcceptingAccumulatedDays);
    }

    [Fact]
    public async Task BriefAcceptingTogglePreservesDaysTowardsRoleExpiry()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var profile = new TrainerProfile
        {
            PTUserId = "trainer", IsActive = true, AcceptingNewMembers = false,
            NotAcceptingSince = today.AddDays(-59), NotAcceptingCountFrom = today.AddDays(-59)
        };
        var trainerRepo = ReadinessRepository.Create<ITrainerRepository>(profile);
        var trainer = new TrainerService(trainerRepo, null!, null!);

        Assert.True((await trainer.SetReadiness(true, "trainer", default)).IsSuccess);
        Assert.True((await trainer.SetReadiness(false, "trainer", default)).IsSuccess);
        var ptRepo = ReadinessRepository.Create<IPersonalTrainingRepository>(profile);
        var pt = new PersonalTrainingService(ptRepo, TimeProvider.System, null!, null!, null!);
        await pt.ExpireInactiveTrainers(today.AddDays(1), default);
        Assert.True(profile.IsActive); // 59 + 1 days is still within the 60-day allowance.
        await pt.ExpireInactiveTrainers(today.AddDays(2), default);
        Assert.False(profile.IsActive); // 59 + 2 days exceeds the allowance despite the toggle.
    }

    [Fact]
    public async Task RunningContractPausesPreviouslyAccumulatedInactiveDays()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var profile = new TrainerProfile
        {
            PTUserId = "trainer", IsActive = true, AcceptingNewMembers = false,
            NotAcceptingSince = today.AddDays(-59), NotAcceptingCountFrom = today.AddDays(-59)
        };
        var trainer = new TrainerService(ReadinessRepository.Create<ITrainerRepository>(profile), null!, null!);
        Assert.True((await trainer.SetReadiness(true, "trainer", default)).IsSuccess);
        Assert.True((await trainer.SetReadiness(false, "trainer", default)).IsSuccess);
        var ptRepo = ReadinessRepository.Create<IPersonalTrainingRepository>(profile);
        var repository = (ReadinessRepository)(object)ptRepo;
        var pt = new PersonalTrainingService(ptRepo, TimeProvider.System, null!, null!, null!);

        repository.Running = true;
        await pt.ExpireInactiveTrainers(today.AddDays(20), default);
        Assert.True(profile.IsActive);
        repository.Running = false;
        await pt.ExpireInactiveTrainers(today.AddDays(21), default);
        Assert.True(profile.IsActive);
        await pt.ExpireInactiveTrainers(today.AddDays(22), default);
        Assert.False(profile.IsActive);
    }

    public class ReadinessRepository : DispatchProxy
    {
        public TrainerProfile Profile { get; set; } = null!;
        public bool Running { get; set; }

        public static T Create<T>(TrainerProfile profile) where T : class
        {
            var repository = Create<T, ReadinessRepository>();
            ((ReadinessRepository)(object)repository).Profile = profile;
            return repository;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "RunExclusiveAsync" => ((Delegate)args![0]!).DynamicInvoke(),
            "UserHasActiveRoleAsync" => Task.FromResult(true),
            "GetProfileByUserAsync" => Task.FromResult<TrainerProfile?>(Profile),
            "HasRunningPTContractAsync" or "HasRunningContractAsync" => Task.FromResult(Running),
            "GetNotAcceptingProfilesAsync" => Task.FromResult<IReadOnlyList<TrainerProfile>>(
                Profile.AcceptingNewMembers || !Profile.IsActive ? [] : [Profile]),
            "GetRoleIdAsync" => Task.FromResult<int?>(1),
            "GetRoleAssignmentAsync" => Task.FromResult<ApplicationUserRole?>(null),
            "Add" or "Remove" => null,
            "SaveChangesAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name)
        };
    }
}
