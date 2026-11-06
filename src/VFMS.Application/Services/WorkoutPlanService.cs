using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Workout;
using VFMS.Contracts.Workout.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-49/UC-58 Generate and UC-50/UC-59 Manage Personalized Workout Plan.
/// GBR-93 survey required before generation; GBR-94 one active plan (previous one archived);
/// GBR-95 standard fallback plan when Gemini is unavailable.
/// </summary>
public sealed class WorkoutPlanService : IWorkoutPlanService
{
    private const int MaxDays = 7;
    private const int MaxItemsPerDay = 12;

    private readonly IWorkoutRepository _repository;
    private readonly IWorkoutPlanGenerator _generator;
    private readonly TimeProvider _clock;

    public WorkoutPlanService(IWorkoutRepository repository, IWorkoutPlanGenerator generator, TimeProvider clock)
    {
        _repository = repository;
        _generator = generator;
        _clock = clock;
    }

    // ── Survey ───────────────────────────────────────────────────

    public async Task<Result<FitnessSurveyResponse>> GetSurveyAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetProfileAsync(userId, cancellationToken);
        return Result<FitnessSurveyResponse>.Success(ToResponse(profile));
    }

    public async Task<Result<FitnessSurveyResponse>> SaveSurveyAsync(
        string userId, FitnessSurveyRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetProfileAsync(userId, cancellationToken);
        var isNew = profile is null;
        profile ??= new PhysicalProfile { MemberUserId = userId };

        profile.WeightKg = request.WeightKg;
        profile.HeightCm = request.HeightCm;
        profile.FitnessGoal = Canonical(request.FitnessGoal, WorkoutRules.Goals);
        profile.FitnessLevel = Canonical(request.FitnessLevel, WorkoutRules.Levels);
        profile.WorkoutFrequencyPerWeek = request.WorkoutFrequencyPerWeek;
        profile.Gender = request.Gender is null ? null : Canonical(request.Gender, WorkoutRules.Genders);
        profile.DateOfBirth = request.DateOfBirth;
        profile.HealthConditions = string.IsNullOrWhiteSpace(request.HealthConditions) ? null : request.HealthConditions.Trim();
        profile.UpdatedAt = DateTime.UtcNow;

        if (isNew)
            _repository.Add(profile);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<FitnessSurveyResponse>.Success(ToResponse(profile));
    }

    // ── Generate ─────────────────────────────────────────────────

    public async Task<Result<WorkoutPlanResponse>> GenerateAsync(
        string userId, GenerateWorkoutPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.ConsentToAiProcessing)
            return Result<WorkoutPlanResponse>.Failure(
                "Please agree to the processing of your fitness survey before generating a plan.", "WORKOUT_CONSENT_REQUIRED");

        var profile = await _repository.GetProfileAsync(userId, cancellationToken);
        if (!IsComplete(profile))
            return Result<WorkoutPlanResponse>.Failure(
                "Complete the fitness survey before generating a workout plan.", "WORKOUT_SURVEY_INCOMPLETE");   // GBR-93

        var catalog = (await _repository.GetActiveExercisesAsync(cancellationToken))
            .Select(e => new CatalogExercise(e.Id, e.Name, e.MuscleGroup, e.DifficultyLevel))
            .ToArray();
        var survey = Anonymise(profile!);

        GeneratedPlan? generated = null;
        if (catalog.Length > 0)
        {
            try
            {
                generated = Sanitize(await _generator.GenerateAsync(survey, catalog, cancellationToken), catalog);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                generated = null;   // GBR-95: any AI failure falls back to a standard plan.
            }
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var plan = new WorkoutPlan
        {
            MemberUserId = userId,
            PhysicalProfileId = profile!.Id == 0 ? null : profile.Id,
            FitnessGoal = profile.FitnessGoal,
            PlanStatus = WorkoutPlanStatus.Active,
            GeneratedAt = now
        };

        if (generated is not null)
        {
            plan.Name = generated.Name;
            plan.GeneratedByAiModel = _generator.ModelName;
            foreach (var item in generated.Items)
                plan.PlanItems.Add(new PlanItem
                {
                    DayNumber = item.DayNumber, OrderInDay = item.OrderInDay, ExerciseId = item.ExerciseId,
                    Sets = item.Sets, Reps = item.Reps, DurationMinutes = item.DurationMinutes, Note = item.Note
                });
        }
        else
        {
            var template = await _repository.GetFallbackTemplateAsync(profile.FitnessGoal!, cancellationToken)
                           ?? await _repository.GetFallbackTemplateAsync(nameof(FitnessGoal.GeneralFitness), cancellationToken);
            if (template is null || template.PlanItems.Count == 0)
                return Result<WorkoutPlanResponse>.Failure(
                    "The workout plan service is unavailable and no standard plan is configured. Please try again later.",
                    "WORKOUT_GENERATION_UNAVAILABLE", 503);

            plan.Name = template.Name ?? "Standard plan";
            plan.IsFallback = true;
            foreach (var item in template.PlanItems.OrderBy(i => i.DayNumber).ThenBy(i => i.OrderInDay))
                plan.PlanItems.Add(new PlanItem
                {
                    DayNumber = item.DayNumber, OrderInDay = item.OrderInDay, ExerciseId = item.ExerciseId,
                    Sets = item.Sets, Reps = item.Reps, DurationMinutes = item.DurationMinutes, Note = item.Note
                });
        }

        var previous = await _repository.GetActivePlansAsync(userId, cancellationToken);
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            foreach (var old in previous)   // GBR-94
            {
                old.PlanStatus = WorkoutPlanStatus.Archived;
                old.UpdatedAt = now;
            }
            _repository.Add(plan);
            _repository.Add(AuditedWriter.Notify(userId, null,
                plan.IsFallback ? "Standard workout plan ready" : "Your workout plan is ready",
                plan.IsFallback
                    ? "The AI service is unavailable, so we prepared a standard plan for your goal."
                    : "Your personalised workout plan has been generated.",
                nameof(WorkoutPlan), NotificationType.WorkoutPlanGenerated));
            await _repository.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        var saved = await _repository.GetPlanAsync(plan.Id, cancellationToken) ?? plan;
        return Result<WorkoutPlanResponse>.Success(ToResponse(saved), 201);
    }

    // ── Manage ───────────────────────────────────────────────────

    public async Task<Result<WorkoutPlanResponse>> GetActivePlanAsync(string userId, CancellationToken cancellationToken = default)
    {
        var active = (await _repository.GetActivePlansAsync(userId, cancellationToken))
            .OrderByDescending(p => p.GeneratedAt).FirstOrDefault();
        if (active is null)
            return Result<WorkoutPlanResponse>.Failure("You do not have an active workout plan yet.", "WORKOUT_NO_ACTIVE_PLAN", 404);
        var plan = await _repository.GetPlanAsync(active.Id, cancellationToken) ?? active;
        return Result<WorkoutPlanResponse>.Success(ToResponse(plan));
    }

    public async Task<Result<WorkoutPlanResponse>> GetPlanAsync(string userId, int planId, CancellationToken cancellationToken = default)
    {
        var plan = await _repository.GetPlanAsync(planId, cancellationToken);
        return plan is null || plan.MemberUserId != userId || plan.IsTemplate
            ? PlanNotFound<WorkoutPlanResponse>()
            : Result<WorkoutPlanResponse>.Success(ToResponse(plan));
    }

    public async Task<Result<IReadOnlyList<WorkoutPlanSummaryResponse>>> GetHistoryAsync(string userId, CancellationToken cancellationToken = default)
    {
        var plans = await _repository.GetPlanHistoryAsync(userId, cancellationToken);
        return Result<IReadOnlyList<WorkoutPlanSummaryResponse>>.Success(plans
            .Select(p => new WorkoutPlanSummaryResponse(p.Id, p.Name, p.PlanStatus.ToString(), p.FitnessGoal, p.IsFallback,
                p.GeneratedAt, p.PlanItems.Count(i => i.IsCompleted), p.PlanItems.Count))
            .ToArray());
    }

    public async Task<Result<WorkoutPlanItemResponse>> SetItemCompletionAsync(
        string userId, int planId, int itemId, bool isCompleted, CancellationToken cancellationToken = default)
    {
        var (item, error) = await FindEditableItemAsync(userId, planId, itemId, cancellationToken);
        if (error is not null)
            return error;
        item!.IsCompleted = isCompleted;
        item.CompletedAt = isCompleted ? _clock.GetUtcNow().UtcDateTime : null;
        item.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<WorkoutPlanItemResponse>.Success(ToResponse(item));
    }

    public async Task<Result<WorkoutPlanItemResponse>> UpdateItemAsync(
        string userId, int planId, int itemId, UpdatePlanItemRequest request, CancellationToken cancellationToken = default)
    {
        var (item, error) = await FindEditableItemAsync(userId, planId, itemId, cancellationToken);
        if (error is not null)
            return error;
        var exercise = (await _repository.GetActiveExercisesAsync(cancellationToken)).FirstOrDefault(e => e.Id == request.ExerciseId);
        if (exercise is null)
            return Result<WorkoutPlanItemResponse>.Failure("Exercise not found.", "WORKOUT_EXERCISE_NOT_FOUND");

        item!.ExerciseId = exercise.Id;
        item.Exercise = exercise;
        item.Sets = request.Sets;
        item.Reps = request.Reps;
        item.DurationMinutes = request.DurationMinutes;
        item.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        item.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<WorkoutPlanItemResponse>.Success(ToResponse(item));
    }

    public async Task<Result<IReadOnlyList<ExerciseResponse>>> GetExercisesAsync(CancellationToken cancellationToken = default)
    {
        var exercises = await _repository.GetActiveExercisesAsync(cancellationToken);
        return Result<IReadOnlyList<ExerciseResponse>>.Success(exercises
            .Select(e => new ExerciseResponse(e.Id, e.Name, e.MuscleGroup, e.DifficultyLevel, e.Description)).ToArray());
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<(PlanItem? Item, Result<WorkoutPlanItemResponse>? Error)> FindEditableItemAsync(
        string userId, int planId, int itemId, CancellationToken cancellationToken)
    {
        var plan = await _repository.GetPlanAsync(planId, cancellationToken);
        if (plan is null || plan.MemberUserId != userId || plan.IsTemplate)
            return (null, PlanNotFound<WorkoutPlanItemResponse>());
        if (plan.PlanStatus != WorkoutPlanStatus.Active)
            return (null, Result<WorkoutPlanItemResponse>.Failure("Archived plans cannot be changed.", "WORKOUT_PLAN_ARCHIVED", 409));
        var item = plan.PlanItems.FirstOrDefault(i => i.Id == itemId);
        return item is null
            ? (null, Result<WorkoutPlanItemResponse>.Failure("Exercise not found in this plan.", "WORKOUT_ITEM_NOT_FOUND", 404))
            : (item, null);
    }

    public static bool IsComplete(PhysicalProfile? p) =>
        p is { WeightKg: not null, HeightCm: not null, WorkoutFrequencyPerWeek: not null } &&
        !string.IsNullOrWhiteSpace(p.FitnessGoal) && !string.IsNullOrWhiteSpace(p.FitnessLevel);

    private AnonymisedSurvey Anonymise(PhysicalProfile p)
    {
        int? age = null;
        if (p.DateOfBirth is { } dob)
        {
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            age = today.Year - dob.Year - (today < dob.AddYears(today.Year - dob.Year) ? 1 : 0);
        }
        return new AnonymisedSurvey(age, p.Gender, p.WeightKg!.Value, p.HeightCm!.Value, p.FitnessGoal!, p.FitnessLevel!,
            p.WorkoutFrequencyPerWeek!.Value, RedactContactDetails(p.HealthConditions));
    }

    /// <summary>Keeps only items that reference real catalog exercises and sane values; null if nothing usable remains.</summary>
    public static GeneratedPlan? Sanitize(GeneratedPlan? plan, IReadOnlyList<CatalogExercise> catalog)
    {
        if (plan is null)
            return null;
        var ids = catalog.Select(c => c.Id).ToHashSet();
        var items = plan.Items
            .Where(i => ids.Contains(i.ExerciseId) && i.DayNumber is >= 1 and <= MaxDays)
            .Where(i => i.Reps is > 0 and <= 100 || i.DurationMinutes is > 0 and <= 180)
            .GroupBy(i => i.DayNumber)
            .SelectMany(day => day.OrderBy(i => i.OrderInDay).Take(MaxItemsPerDay).Select((i, index) => i with
            {
                OrderInDay = index + 1,
                Sets = i.Sets is > 0 and <= 20 ? i.Sets : null,
                Reps = i.Reps is > 0 and <= 100 ? i.Reps : null,
                DurationMinutes = i.DurationMinutes is > 0 and <= 180 ? i.DurationMinutes : null,
                Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim()[..Math.Min(i.Note.Trim().Length, 500)]
            }))
            .ToArray();
        if (items.Length == 0)
            return null;
        var name = string.IsNullOrWhiteSpace(plan.Name) ? "Personalised plan" : plan.Name.Trim()[..Math.Min(plan.Name.Trim().Length, 200)];
        return new GeneratedPlan(name, items);
    }

    private static readonly System.Text.RegularExpressions.Regex EmailPattern =
        new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PhonePattern =
        new(@"\+?\d[\d\s.-]{7,}\d", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Removes e-mail addresses and phone-like numbers from free text before it leaves VFMS (PDPL).</summary>
    public static string? RedactContactDetails(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var cleaned = PhonePattern.Replace(EmailPattern.Replace(text, "[removed]"), "[removed]");
        return cleaned.Trim();
    }

    private static string Canonical(string value, string[] allowed) =>
        allowed.First(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static FitnessSurveyResponse ToResponse(PhysicalProfile? p) => p is null
        ? new FitnessSurveyResponse(null, null, null, null, null, null, null, null, false)
        : new FitnessSurveyResponse(p.WeightKg, p.HeightCm, p.FitnessGoal, p.FitnessLevel, p.WorkoutFrequencyPerWeek,
            p.Gender, p.DateOfBirth, p.HealthConditions, IsComplete(p));

    private static WorkoutPlanResponse ToResponse(WorkoutPlan p)
    {
        var items = p.PlanItems.OrderBy(i => i.DayNumber).ThenBy(i => i.OrderInDay).Select(ToResponse).ToArray();
        return new WorkoutPlanResponse(p.Id, p.Name, p.PlanStatus.ToString(), p.FitnessGoal, p.IsFallback, p.GeneratedByAiModel,
            p.GeneratedAt, items.Count(i => i.IsCompleted), items.Length, items);
    }

    private static WorkoutPlanItemResponse ToResponse(PlanItem i) => new(
        i.Id, i.DayNumber, i.OrderInDay, i.ExerciseId, i.Exercise?.Name ?? string.Empty, i.Exercise?.MuscleGroup,
        i.Exercise?.DifficultyLevel, i.Exercise?.ImageUrl, i.Exercise?.VideoUrl, i.Sets, i.Reps, i.DurationMinutes, i.Note,
        i.IsCompleted, i.CompletedAt);

    private static Result<T> PlanNotFound<T>() => Result<T>.Failure("Workout plan not found.", "WORKOUT_PLAN_NOT_FOUND", 404);
}
