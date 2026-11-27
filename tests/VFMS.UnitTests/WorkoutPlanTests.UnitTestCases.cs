using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Workout;
using VFMS.Contracts.Workout.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>
/// Report 5.1 unit test cases for the saveFitnessSurvey, generateWorkoutPlan and setPlanItemCompletion sheets,
/// one test per UTCID. Now is Saturday 07/11/2026 09:00 gym time; the gym week started Monday 02/11.
/// </summary>
public sealed partial class WorkoutPlanTests
{
    private static FitnessSurveyRequest Survey(decimal weight = 70, decimal height = 170, string goal = "MuscleGain",
        int frequency = 4, string? gender = "Female", string? health = null) =>
        new(weight, height, goal, "Intermediate", frequency, gender, new DateOnly(1999, 3, 1), health);

    private static bool SurveyIsValid(FitnessSurveyRequest request) => new FitnessSurveyRequestValidator().Validate(request).IsValid;

    // ── WorkoutPlanService.SaveSurveyAsync + FitnessSurveyRequestValidator (UC-48) ──

    [Fact]
    public async Task SaveSurvey_UTCID01_FirstSurveyCreatesTheProfile()
    {
        var (service, repo, _) = Create(null);

        var result = await service.SaveSurveyAsync("u1", Survey(goal: " muscleGAIN "));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(("u1", "MuscleGain", 70m), (repo.Profile!.MemberUserId, repo.Profile.FitnessGoal, repo.Profile.WeightKg!.Value));
    }

    [Fact]
    public async Task SaveSurvey_UTCID02_SecondSurveyUpdatesTheSameProfile()
    {
        var (service, repo, _) = Create(CompleteProfile());
        var existing = repo.Profile;

        await service.SaveSurveyAsync("u1", Survey(weight: 76));

        Assert.Same(existing, repo.Profile);
        Assert.Equal(76m, repo.Profile!.WeightKg);
    }

    [Fact]
    public async Task SaveSurvey_UTCID03_BlankHealthNotesAreStoredAsEmpty()
    {
        var (service, repo, _) = Create(null);

        await service.SaveSurveyAsync("u1", Survey(health: "   "));

        Assert.Null(repo.Profile!.HealthConditions);
    }

    [Fact]
    public void SaveSurvey_UTCID04_WeightOf20KgIsAccepted() => Assert.True(SurveyIsValid(Survey(weight: 20)));

    [Fact]
    public void SaveSurvey_UTCID05_WeightBelow20KgIsRejected() => Assert.False(SurveyIsValid(Survey(weight: 19.9m)));

    [Fact]
    public void SaveSurvey_UTCID06_WeightAbove300KgIsRejected() => Assert.False(SurveyIsValid(Survey(weight: 300.1m)));

    [Fact]
    public void SaveSurvey_UTCID07_HeightOf250CmIsAccepted() => Assert.True(SurveyIsValid(Survey(height: 250)));

    [Fact]
    public void SaveSurvey_UTCID08_HeightBelow100CmIsRejected() => Assert.False(SurveyIsValid(Survey(height: 99)));

    [Fact]
    public void SaveSurvey_UTCID09_ZeroWorkoutsPerWeekIsRejected() => Assert.False(SurveyIsValid(Survey(frequency: 0)));

    [Fact]
    public void SaveSurvey_UTCID10_SevenWorkoutsPerWeekIsAccepted() => Assert.True(SurveyIsValid(Survey(frequency: 7)));

    [Fact]
    public void SaveSurvey_UTCID11_UnknownGoalIsRejected() => Assert.False(SurveyIsValid(Survey(goal: "Sleep")));

    [Fact]
    public void SaveSurvey_UTCID12_UnknownGenderIsRejected() => Assert.False(SurveyIsValid(Survey(gender: "X")));

    [Fact]
    public void SaveSurvey_UTCID13_HealthNotesOver500CharactersAreRejected() =>
        Assert.False(SurveyIsValid(Survey(health: new string('a', 501))));

    // ── WorkoutPlanService.GenerateAsync (UC-49, GBR-93..95) ────

    [Fact]
    public async Task GeneratePlan_UTCID01_AiPlanIsSavedAndTheOldPlanArchived()
    {
        var (service, repo, generator) = Create(CompleteProfile());
        var old = new WorkoutPlan { Id = 1, MemberUserId = "u1", PlanStatus = WorkoutPlanStatus.Active };
        repo.Plans.Add(old);
        generator.Reply = new GeneratedPlan("Lean week", [new(1, 1, 10, 3, 12, null, null)]);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.Data!.IsFallback);
        Assert.Equal(WorkoutPlanStatus.Archived, old.PlanStatus);
        Assert.Single(repo.Notifications);
    }

    [Fact]
    public async Task GeneratePlan_UTCID02_NoConsentStopsBeforeTheAi()
    {
        var (service, _, generator) = Create(CompleteProfile());

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(false));

        Assert.Equal("WORKOUT_CONSENT_REQUIRED", result.ErrorCode);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task GeneratePlan_UTCID03_NoSurveyYetIsRejected()
    {
        var (service, _, generator) = Create(null);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.Equal("WORKOUT_SURVEY_INCOMPLETE", result.ErrorCode);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task GeneratePlan_UTCID04_SurveyMissingTheLevelIsRejected()
    {
        var profile = CompleteProfile();
        profile.FitnessLevel = null;
        var (service, _, _) = Create(profile);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.Equal("WORKOUT_SURVEY_INCOMPLETE", result.ErrorCode);
    }

    [Fact]
    public async Task GeneratePlan_UTCID05_AiGivingNoAnswerFallsBackToTheTemplate()
    {
        var (service, repo, generator) = Create(CompleteProfile());
        generator.Reply = null;

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.Data!.IsFallback);
        Assert.Equal("Standard fat-loss", Assert.Single(repo.Plans, p => !p.IsTemplate).Name);
    }

    [Fact]
    public async Task GeneratePlan_UTCID06_AiErrorFallsBackToTheTemplate()
    {
        var (service, _, generator) = Create(CompleteProfile());
        generator.Throw = true;

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Data!.IsFallback);
    }

    [Fact]
    public async Task GeneratePlan_UTCID07_AiInventingExercisesFallsBackToTheTemplate()
    {
        var (service, _, generator) = Create(CompleteProfile());
        generator.Reply = new GeneratedPlan("bad", [new(1, 1, 999, 3, 12, null, null)]);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.Data!.IsFallback);
    }

    [Fact]
    public async Task GeneratePlan_UTCID08_SurveySentToTheAiHasNoPersonalIdentifiers()
    {
        var (service, repo, generator) = Create(CompleteProfile());
        generator.Reply = new GeneratedPlan("p", [new(1, 1, 10, 3, 12, null, null)]);

        await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.Equal(26, generator.LastSurvey!.Age);
        Assert.DoesNotContain("u1", generator.LastSurvey.ToString());
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, repo.Profile!.AiConsentGrantedAt);
    }

    // ── WorkoutPlanService.SetItemCompletionAsync (UC-50) ───────

    /// <summary>Member u1's active plan 5 with item 50 (Squat 3 x 12).</summary>
    private static (WorkoutPlanService Service, RepoFake Repo, WorkoutPlan Plan, PlanItem Item) PlanWithItem()
    {
        var (service, repo, _) = Create(CompleteProfile());
        var item = new PlanItem { Id = 50, PlanId = 5, ExerciseId = 10, Sets = 3, Reps = 12 };
        var plan = new WorkoutPlan { Id = 5, MemberUserId = "u1", PlanStatus = WorkoutPlanStatus.Active };
        plan.PlanItems.Add(item);
        repo.Plans.Add(plan);
        return (service, repo, plan, item);
    }

    private static bool CompletionIsValid(SetPlanItemCompletionRequest request) =>
        new SetPlanItemCompletionRequestValidator().Validate(request).IsValid;

    [Fact]
    public async Task SetCompletion_UTCID01_TickingLogsTodaysResult()
    {
        var (service, _, _, item) = PlanWithItem();

        var result = await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(true, 3, 12, 40m));

        Assert.True(result.Data!.IsCompleted);
        var log = Assert.Single(item.Logs);
        Assert.Equal((GymToday, 3, 12, 40m), (log.LoggedOn, log.SetsDone!.Value, log.RepsDone!.Value, log.WeightKg!.Value));
    }

    [Fact]
    public async Task SetCompletion_UTCID02_TickingTwiceTheSameDayUpdatesOneLog()
    {
        var (service, _, _, item) = PlanWithItem();
        await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(true, 3, 12, 40m));

        await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(true, 3, 12, 42.5m));

        Assert.Equal(42.5m, Assert.Single(item.Logs).WeightKg);
    }

    [Fact]
    public async Task SetCompletion_UTCID03_UntickingRemovesThisWeeksLogFromMonday()
    {
        var (service, repo, _, item) = PlanWithItem();
        item.Logs.Add(new WorkoutLog { Id = 1, PlanItemId = 50, LoggedOn = new DateOnly(2026, 11, 2) });   // Monday, this week

        var result = await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(false));

        Assert.False(result.Data!.IsCompleted);
        Assert.Empty(item.Logs);
        Assert.Single(repo.Removed);
    }

    [Fact]
    public async Task SetCompletion_UTCID04_UntickingKeepsLastSundaysLog()
    {
        var (service, _, _, item) = PlanWithItem();
        item.Logs.Add(new WorkoutLog { Id = 1, PlanItemId = 50, LoggedOn = new DateOnly(2026, 11, 1) });   // Sunday, last week

        await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(false));

        Assert.Single(item.Logs);
    }

    [Fact]
    public async Task SetCompletion_UTCID05_AnotherMembersPlanIsNotFound()
    {
        var (service, _, _, _) = PlanWithItem();

        var result = await service.SetItemCompletionAsync("u2", 5, 50, new SetPlanItemCompletionRequest(true));

        Assert.Equal((404, "WORKOUT_PLAN_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SetCompletion_UTCID06_ArchivedPlanCannotBeChanged()
    {
        var (service, _, plan, item) = PlanWithItem();
        plan.PlanStatus = WorkoutPlanStatus.Archived;

        var result = await service.SetItemCompletionAsync("u1", 5, 50, new SetPlanItemCompletionRequest(true));

        Assert.Equal((409, "WORKOUT_PLAN_ARCHIVED"), (result.StatusCode, result.ErrorCode));
        Assert.Empty(item.Logs);
    }

    [Fact]
    public async Task SetCompletion_UTCID07_ItemOutsideThePlanIsNotFound()
    {
        var (service, _, _, _) = PlanWithItem();

        var result = await service.SetItemCompletionAsync("u1", 5, 99, new SetPlanItemCompletionRequest(true));

        Assert.Equal((404, "WORKOUT_ITEM_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SetCompletion_UTCID08_FallbackTemplateIsNotAMemberPlan()
    {
        var (service, repo, _, _) = PlanWithItem();
        var template = repo.Plans.Single(p => p.IsTemplate);
        template.MemberUserId = "u1";

        var result = await service.SetItemCompletionAsync("u1", template.Id, 50, new SetPlanItemCompletionRequest(true));

        Assert.Equal((404, "WORKOUT_PLAN_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public void SetCompletion_UTCID09_TwentySetsIsAccepted() => Assert.True(CompletionIsValid(new(true, SetsDone: 20)));

    [Fact]
    public void SetCompletion_UTCID10_TwentyOneSetsIsRejected() => Assert.False(CompletionIsValid(new(true, SetsDone: 21)));

    [Fact]
    public void SetCompletion_UTCID11_ZeroRepsIsRejected() => Assert.False(CompletionIsValid(new(true, RepsDone: 0)));

    [Fact]
    public void SetCompletion_UTCID12_ZeroKgWeightIsAccepted() => Assert.True(CompletionIsValid(new(true, WeightKg: 0m)));
}
