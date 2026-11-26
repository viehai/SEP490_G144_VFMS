namespace VFMS.Contracts.Workout;

/// <summary>Fixed values for the exercise catalog. The plan's "Đổi bài tập" filter matches muscle groups exactly.</summary>
public static class ExerciseCatalog
{
    public static readonly IReadOnlyList<string> MuscleGroups =
        ["Cardio", "Chest", "Back", "Shoulders", "Arms", "Legs", "Hamstrings", "Core", "Mobility"];

    public static readonly IReadOnlyList<string> Levels = ["Beginner", "Intermediate", "Advanced"];

    public static string MuscleGroupLabel(string? group) => group switch
    {
        "Cardio" => "Tim mạch",
        "Chest" => "Ngực",
        "Back" => "Lưng",
        "Shoulders" => "Vai",
        "Arms" => "Tay",
        "Legs" => "Chân",
        "Hamstrings" => "Đùi sau",
        "Core" => "Bụng / lõi",
        "Mobility" => "Giãn cơ",
        null or "" => "Khác",
        _ => group
    };

    public static string LevelLabel(string? level) => level switch
    {
        "Beginner" => "Cơ bản",
        "Intermediate" => "Trung bình",
        "Advanced" => "Nâng cao",
        _ => "—"
    };
}

/// <summary>Manager: create or edit a catalog exercise.</summary>
public sealed record ExerciseRequest(
    string Name, string MuscleGroup, string DifficultyLevel, string? Description = null, string? ImageUrl = null, string? VideoUrl = null);

/// <summary>Manager catalog row. PlanUses counts plan items using it, so the Manager sees why it is deactivated, not deleted.</summary>
public sealed record ManagerExerciseResponse(
    int Id, string Name, string? MuscleGroup, string? DifficultyLevel, string? Description, string? ImageUrl, string? VideoUrl,
    bool IsActive, int PlanUses);

public sealed record SetExerciseActiveRequest(bool IsActive);
