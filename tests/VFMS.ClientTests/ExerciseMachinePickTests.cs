using VFMS.Client.Pages.Manager;
using VFMS.Client.Pages.Member;
using VFMS.Contracts.Workout;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>The exercise's default machine: the Manager's picker and the member's "Đổi bài tập" preselection.</summary>
public sealed class ExerciseMachinePickTests
{
    private static readonly ExerciseMachineOption[] Options = [new(4, "Xe đạp tập", 2, 1), new(7, "Máy đạp đùi", 1, 1)];

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(4, "Xe đạp tập", 4)]
    [InlineData(3, "Xe đạp tập", 4)]     // a stored broken unit maps to the option with its name
    [InlineData(9, "Máy cũ", null)]      // a machine that is gone shows "Không dùng máy"
    public void EditFormMapsTheStoredUnitToItsMachineName(int? stored, string? name, int? expected) =>
        Assert.Equal(expected, ExerciseCatalogManagement.MachineOptionFor(Options, stored, name));

    [Fact]
    public void SwapPreselectsAWorkingUnitOfTheNewExercisesMachine()
    {
        ExerciseResponse[] exercises = [new(1, "Stationary Bike", "Cardio", "Beginner", null, "Xe đạp tập"), new(2, "Plank", "Core", "Beginner", null)];
        WorkoutEquipmentResponse[] working = [new(5, "Máy đạp đùi", "ST-0001"), new(8, "Xe đạp tập", "CD-0004")];

        Assert.Equal(8, WorkoutPlan.DefaultMachine(exercises, working, 1));
        Assert.Null(WorkoutPlan.DefaultMachine(exercises, working, 2));
        Assert.Null(WorkoutPlan.DefaultMachine(exercises, [working[0]], 1));   // no working bike
    }
}
