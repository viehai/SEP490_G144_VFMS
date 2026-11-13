using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Data;

/// <summary>
/// Seeds the exercise catalog used by Gemini (API-02) and one standard template plan per fitness goal,
/// used when the AI is unavailable (GBR-95). Runs only when the tables are empty.
/// </summary>
internal static class WorkoutSeed
{
    private static readonly (string Name, string Muscle, string Level, string Description)[] Exercises =
    [
        ("Treadmill Walk", "Cardio", "Beginner", "Brisk walk at a slight incline."),
        ("Stationary Bike", "Cardio", "Beginner", "Steady pace cycling."),
        ("Rowing Machine", "Cardio", "Intermediate", "Full-body rowing at moderate intensity."),
        ("Jump Rope", "Cardio", "Intermediate", "Continuous skipping intervals."),
        ("Goblet Squat", "Legs", "Beginner", "Squat holding a dumbbell at the chest."),
        ("Leg Press", "Legs", "Beginner", "Machine leg press."),
        ("Barbell Back Squat", "Legs", "Advanced", "Back squat with a barbell."),
        ("Romanian Deadlift", "Hamstrings", "Intermediate", "Hip hinge with dumbbells or barbell."),
        ("Walking Lunge", "Legs", "Beginner", "Alternating forward lunges."),
        ("Push-up", "Chest", "Beginner", "Bodyweight push-up; knees down to make it easier."),
        ("Dumbbell Bench Press", "Chest", "Intermediate", "Flat bench press with dumbbells."),
        ("Barbell Bench Press", "Chest", "Advanced", "Flat bench press with a barbell."),
        ("Lat Pulldown", "Back", "Beginner", "Cable pulldown to the upper chest."),
        ("Seated Cable Row", "Back", "Beginner", "Cable row to the stomach."),
        ("Pull-up", "Back", "Advanced", "Bodyweight pull-up; use the assisted machine if needed."),
        ("Dumbbell Shoulder Press", "Shoulders", "Intermediate", "Seated overhead press."),
        ("Lateral Raise", "Shoulders", "Beginner", "Dumbbell side raise."),
        ("Dumbbell Curl", "Arms", "Beginner", "Biceps curl."),
        ("Triceps Rope Pushdown", "Arms", "Beginner", "Cable pushdown with a rope."),
        ("Plank", "Core", "Beginner", "Hold a straight body on forearms."),
        ("Dead Bug", "Core", "Beginner", "Alternate arm and leg extensions lying on the back."),
        ("Hanging Knee Raise", "Core", "Intermediate", "Raise knees while hanging from a bar."),
        ("Hip Mobility Flow", "Mobility", "Beginner", "Hip circles, 90/90 switches and lunges with reach."),
        ("Full-body Stretch", "Mobility", "Beginner", "Hamstring, chest, shoulder and calf stretches."),
    ];

    // (exercise name, sets, reps, minutes) per day for each goal.
    private static readonly Dictionary<FitnessGoal, (string Name, (string Ex, int? Sets, int? Reps, int? Min)[][] Days)> Templates = new()
    {
        [FitnessGoal.WeightLoss] = ("Standard fat-loss plan",
        [
            [("Treadmill Walk", null, null, 20), ("Goblet Squat", 3, 12, null), ("Push-up", 3, 10, null), ("Seated Cable Row", 3, 12, null), ("Plank", 3, null, 1)],
            [("Stationary Bike", null, null, 25), ("Walking Lunge", 3, 12, null), ("Lat Pulldown", 3, 12, null), ("Dead Bug", 3, 12, null)],
            [("Rowing Machine", null, null, 15), ("Leg Press", 3, 15, null), ("Dumbbell Shoulder Press", 3, 12, null), ("Full-body Stretch", null, null, 10)],
        ]),
        [FitnessGoal.MuscleGain] = ("Standard muscle-gain plan",
        [
            [("Dumbbell Bench Press", 4, 10, null), ("Seated Cable Row", 4, 10, null), ("Dumbbell Shoulder Press", 3, 10, null), ("Triceps Rope Pushdown", 3, 12, null)],
            [("Goblet Squat", 4, 10, null), ("Romanian Deadlift", 4, 10, null), ("Leg Press", 3, 12, null), ("Plank", 3, null, 1)],
            [("Lat Pulldown", 4, 10, null), ("Push-up", 3, 12, null), ("Lateral Raise", 3, 15, null), ("Dumbbell Curl", 3, 12, null)],
        ]),
        [FitnessGoal.Endurance] = ("Standard endurance plan",
        [
            [("Rowing Machine", null, null, 25), ("Walking Lunge", 3, 15, null), ("Plank", 3, null, 1)],
            [("Stationary Bike", null, null, 35), ("Push-up", 3, 15, null), ("Dead Bug", 3, 15, null)],
            [("Treadmill Walk", null, null, 30), ("Jump Rope", null, null, 10), ("Full-body Stretch", null, null, 10)],
        ]),
        [FitnessGoal.Flexibility] = ("Standard mobility plan",
        [
            [("Hip Mobility Flow", null, null, 15), ("Full-body Stretch", null, null, 15), ("Dead Bug", 3, 10, null)],
            [("Treadmill Walk", null, null, 15), ("Hip Mobility Flow", null, null, 15), ("Plank", 3, null, 1)],
            [("Full-body Stretch", null, null, 20), ("Goblet Squat", 2, 10, null)],
        ]),
        [FitnessGoal.GeneralFitness] = ("Standard full-body plan",
        [
            [("Treadmill Walk", null, null, 10), ("Goblet Squat", 3, 12, null), ("Push-up", 3, 10, null), ("Lat Pulldown", 3, 12, null), ("Plank", 3, null, 1)],
            [("Stationary Bike", null, null, 20), ("Romanian Deadlift", 3, 10, null), ("Dumbbell Shoulder Press", 3, 10, null), ("Dead Bug", 3, 10, null)],
            [("Rowing Machine", null, null, 10), ("Leg Press", 3, 12, null), ("Seated Cable Row", 3, 12, null), ("Full-body Stretch", null, null, 10)],
        ]),
    };

    public static async Task SeedAsync(VfmsDbContext context, string ownerEmail, ILogger logger)
    {
        if (!await context.Exercises.AnyAsync())
        {
            context.Exercises.AddRange(Exercises.Select(e => new Exercise
            {
                Name = e.Name, MuscleGroup = e.Muscle, DifficultyLevel = e.Level, Description = e.Description, IsActive = true
            }));
            await context.SaveChangesAsync();
            logger.LogInformation("Exercise catalog seeded.");
        }

        if (await context.WorkoutPlans.AnyAsync(p => p.IsTemplate))
            return;
        var ownerId = await context.ApplicationUsers.Where(u => u.Email == ownerEmail).Select(u => u.UserId).FirstOrDefaultAsync();
        if (ownerId is null)
            return;

        var ids = await context.Exercises.ToDictionaryAsync(e => e.Name, e => e.Id);
        foreach (var (goal, (name, days)) in Templates)
        {
            var plan = new WorkoutPlan
            {
                MemberUserId = ownerId, Name = name, FitnessGoal = goal.ToString(), IsTemplate = true, IsFallback = true,
                PlanStatus = WorkoutPlanStatus.Active, GeneratedAt = DateTime.UtcNow
            };
            for (var d = 0; d < days.Length; d++)
                for (var i = 0; i < days[d].Length; i++)
                {
                    var (ex, sets, reps, minutes) = days[d][i];
                    if (!ids.TryGetValue(ex, out var exerciseId))
                        continue;
                    plan.PlanItems.Add(new PlanItem
                    {
                        // Spread training days across the week: 1, 3, 5.
                        DayNumber = d * 2 + 1, OrderInDay = i + 1, ExerciseId = exerciseId,
                        Sets = sets, Reps = reps, DurationMinutes = minutes
                    });
                }
            context.WorkoutPlans.Add(plan);
        }
        await context.SaveChangesAsync();
        logger.LogInformation("Standard workout plan templates seeded.");
    }
}
