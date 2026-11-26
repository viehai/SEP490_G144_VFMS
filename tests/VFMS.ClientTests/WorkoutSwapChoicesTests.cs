using VFMS.Client.Pages.Member;
using VFMS.Contracts.Workout;
using Xunit;

namespace VFMS.ClientTests;

public sealed class WorkoutSwapChoicesTests
{
    private static readonly ExerciseResponse[] Exercises =
    [
        new(1, "Stationary Bike", "Cardio", "Beginner", null),
        new(2, "Rowing Machine", "Cardio", "Intermediate", null),
        new(3, "Leg Press", "Legs", "Beginner", null),
        new(4, "Romanian Deadlift", "Hamstrings", "Intermediate", null),
    ];

    [Fact]
    public void SameGroupByDefaultAllGroupsOnRequest()
    {
        Assert.Equal([2, 1], WorkoutPlan.SwapChoices(Exercises, 1, "Cardio", allGroups: false).Select(e => e.Id));
        // All groups: the item's own group first, then the rest.
        Assert.Equal([2, 1, 4, 3], WorkoutPlan.SwapChoices(Exercises, 1, "Cardio", allGroups: true).Select(e => e.Id));
    }

    [Fact]
    public void AGroupWithOnlyTheCurrentExerciseStillListsIt()
    {
        // The page then shows every group, with a hint.
        Assert.Equal([4], WorkoutPlan.SwapChoices(Exercises, 4, "Hamstrings", allGroups: false).Select(e => e.Id));
    }
}
