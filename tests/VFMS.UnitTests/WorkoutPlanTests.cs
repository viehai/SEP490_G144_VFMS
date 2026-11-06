using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Workout;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class WorkoutPlanTests
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 2, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GenerationNeedsConsent()
    {
        var (service, repo, generator) = Create(CompleteProfile());
        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(false));
        Assert.Equal("WORKOUT_CONSENT_REQUIRED", result.ErrorCode);
        Assert.Equal(0, generator.Calls);
        Assert.DoesNotContain(repo.Plans, p => !p.IsTemplate);
    }

    [Fact]
    public async Task GenerationNeedsCompletedSurvey()   // GBR-93
    {
        var profile = CompleteProfile();
        profile.FitnessLevel = null;
        var (service, repo, generator) = Create(profile);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.Equal("WORKOUT_SURVEY_INCOMPLETE", result.ErrorCode);
        Assert.Equal(0, generator.Calls);
        Assert.DoesNotContain(repo.Plans, p => !p.IsTemplate);
    }

    [Fact]
    public async Task AiPlanIsSavedAndPreviousActivePlanArchived()   // GBR-94
    {
        var (service, repo, generator) = Create(CompleteProfile());
        var old = new WorkoutPlan { Id = 1, MemberUserId = "u1", PlanStatus = WorkoutPlanStatus.Active };
        repo.Plans.Add(old);
        generator.Reply = new GeneratedPlan("Lean week", [new(1, 1, 10, 3, 12, null, null), new(1, 2, 11, null, null, 20, null)]);

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(WorkoutPlanStatus.Archived, old.PlanStatus);
        var plan = Assert.Single(repo.Plans, p => !p.IsTemplate && p.PlanStatus == WorkoutPlanStatus.Active);
        Assert.False(plan.IsFallback);
        Assert.Equal("gemini-test", plan.GeneratedByAiModel);
        Assert.Equal(2, plan.PlanItems.Count);
        Assert.Single(repo.Notifications);
    }

    [Fact]
    public async Task AiSurveyContainsNoPersonalIdentifiers()
    {
        var (service, _, generator) = Create(CompleteProfile());
        generator.Reply = new GeneratedPlan("p", [new(1, 1, 10, 3, 12, null, null)]);

        await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        var survey = generator.LastSurvey!;
        Assert.Equal(26, survey.Age);   // born 2000-05-01, today 2026-11-07
        Assert.DoesNotContain("u1", survey.ToString());
    }

    [Fact]
    public async Task FallbackPlanUsedWhenAiUnavailable()   // GBR-95
    {
        var (service, repo, generator) = Create(CompleteProfile());
        generator.Reply = null;

        var result = await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var plan = Assert.Single(repo.Plans, p => !p.IsTemplate);
        Assert.True(plan.IsFallback);
        Assert.Null(plan.GeneratedByAiModel);
        Assert.Equal("Standard fat-loss", plan.Name);
        Assert.Equal(2, plan.PlanItems.Count);
    }

    [Fact]
    public async Task FallbackPlanUsedWhenAiThrowsOrReturnsUnknownExercises()
    {
        var (service, repo, generator) = Create(CompleteProfile());
        generator.Throw = true;
        Assert.True((await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true))).Data!.IsFallback);

        generator.Throw = false;
        generator.Reply = new GeneratedPlan("bad", [new(1, 1, 999, 3, 12, null, null)]);
        Assert.True((await service.GenerateAsync("u1", new GenerateWorkoutPlanRequest(true))).Data!.IsFallback);
        Assert.Single(repo.Plans, p => !p.IsTemplate && p.PlanStatus == WorkoutPlanStatus.Active);
    }

    [Fact]
    public void SanitizeDropsInvalidItemsAndRenumbers()
    {
        var catalog = new[] { new CatalogExercise(10, "Squat", "Legs", "Beginner") };
        var plan = new GeneratedPlan("x",
        [
            new(1, 5, 10, 3, 12, null, null),
            new(1, 9, 10, 99, 12, null, null),      // sets out of range -> null
            new(8, 1, 10, 3, 12, null, null),       // day out of range
            new(2, 1, 10, 3, null, null, null),     // no reps or duration
        ]);

        var clean = WorkoutPlanService.Sanitize(plan, catalog)!;

        Assert.Equal(2, clean.Items.Count);
        Assert.Equal([1, 2], clean.Items.Select(i => i.OrderInDay));
        Assert.Null(clean.Items[1].Sets);
    }

    [Fact]
    public async Task ArchivedPlanCannotBeChangedAndOthersPlansAreHidden()
    {
        var (service, repo, _) = Create(CompleteProfile());
        var archived = new WorkoutPlan { Id = 5, MemberUserId = "u1", PlanStatus = WorkoutPlanStatus.Archived };
        archived.PlanItems.Add(new PlanItem { Id = 50, PlanId = 5, ExerciseId = 10 });
        var other = new WorkoutPlan { Id = 6, MemberUserId = "u2", PlanStatus = WorkoutPlanStatus.Active };
        other.PlanItems.Add(new PlanItem { Id = 60, PlanId = 6, ExerciseId = 10 });
        repo.Plans.AddRange([archived, other]);

        Assert.Equal(409, (await service.SetItemCompletionAsync("u1", 5, 50, true)).StatusCode);
        Assert.Equal(404, (await service.SetItemCompletionAsync("u1", 6, 60, true)).StatusCode);
    }

    [Fact]
    public async Task MarkingItemCompleteRecordsTime()
    {
        var (service, repo, _) = Create(CompleteProfile());
        var plan = new WorkoutPlan { Id = 5, MemberUserId = "u1", PlanStatus = WorkoutPlanStatus.Active };
        var item = new PlanItem { Id = 50, PlanId = 5, ExerciseId = 10 };
        plan.PlanItems.Add(item);
        repo.Plans.Add(plan);

        var result = await service.SetItemCompletionAsync("u1", 5, 50, true);

        Assert.True(result.IsSuccess);
        Assert.True(item.IsCompleted);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, item.CompletedAt);
    }

    private static PhysicalProfile CompleteProfile() => new()
    {
        Id = 3, MemberUserId = "u1", WeightKg = 80, HeightCm = 175, FitnessGoal = "WeightLoss", FitnessLevel = "Beginner",
        WorkoutFrequencyPerWeek = 3, Gender = "Male", DateOfBirth = new DateOnly(2000, 5, 1)
    };

    private static (WorkoutPlanService, RepoFake, GeneratorFake) Create(PhysicalProfile? profile)
    {
        var repo = new RepoFake { Profile = profile };
        repo.Exercises.AddRange([new Exercise { Id = 10, Name = "Squat", IsActive = true }, new Exercise { Id = 11, Name = "Bike", IsActive = true }]);
        var template = new WorkoutPlan { Id = 100, IsTemplate = true, FitnessGoal = "WeightLoss", Name = "Standard fat-loss" };
        template.PlanItems.Add(new PlanItem { DayNumber = 1, OrderInDay = 1, ExerciseId = 10, Sets = 3, Reps = 12 });
        template.PlanItems.Add(new PlanItem { DayNumber = 3, OrderInDay = 1, ExerciseId = 11, DurationMinutes = 20 });
        repo.Plans.Add(template);
        var generator = new GeneratorFake();
        return (new WorkoutPlanService(repo, generator, Clock), repo, generator);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GeneratorFake : IWorkoutPlanGenerator
    {
        public GeneratedPlan? Reply { get; set; }
        public bool Throw { get; set; }
        public int Calls { get; private set; }
        public AnonymisedSurvey? LastSurvey { get; private set; }
        public string ModelName => "gemini-test";

        public Task<GeneratedPlan?> GenerateAsync(AnonymisedSurvey survey, IReadOnlyList<CatalogExercise> catalog, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSurvey = survey;
            return Throw ? throw new HttpRequestException("down") : Task.FromResult(Reply);
        }
    }

    private sealed class RepoFake : IWorkoutRepository
    {
        public PhysicalProfile? Profile { get; set; }
        public List<Exercise> Exercises { get; } = [];
        public List<WorkoutPlan> Plans { get; } = [];
        public List<Notification> Notifications { get; } = [];
        private int _nextId = 1000;

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            switch (entity)
            {
                case WorkoutPlan plan: plan.Id = _nextId++; Plans.Add(plan); break;
                case Notification n: Notifications.Add(n); break;
                case PhysicalProfile p: Profile = p; break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<PhysicalProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile?.MemberUserId == userId ? Profile : null);
        public Task<IReadOnlyList<Exercise>> GetActiveExercisesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Exercise>>(Exercises);
        public Task<WorkoutPlan?> GetFallbackTemplateAsync(string fitnessGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.FirstOrDefault(p => p.IsTemplate && p.FitnessGoal == fitnessGoal));
        public Task<IReadOnlyList<WorkoutPlan>> GetActivePlansAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkoutPlan>>(Plans.Where(p => p.MemberUserId == userId && !p.IsTemplate && p.PlanStatus == WorkoutPlanStatus.Active).ToArray());
        public Task<WorkoutPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.FirstOrDefault(p => p.Id == planId));
        public Task<IReadOnlyList<WorkoutPlan>> GetPlanHistoryAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkoutPlan>>(Plans.Where(p => p.MemberUserId == userId && !p.IsTemplate).ToArray());
    }
}
