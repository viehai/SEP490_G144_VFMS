using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Workout;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// Manager's exercise catalog: the exercises Gemini builds plans from and members swap between.
/// Exercises are deactivated, never deleted, because existing plan items point at them.
/// </summary>
public sealed class ExerciseCatalogService : IExerciseCatalogService
{
    private readonly IExerciseCatalogRepository _repository;

    public ExerciseCatalogService(IExerciseCatalogRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ManagerExerciseResponse>>> ListAsync(CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<ManagerExerciseResponse>>.Success(
            (await _repository.ListAsync(cancellationToken)).Select(x => ToResponse(x.Exercise, x.PlanUses)).ToArray());

    /// <summary>One option per machine name (like the AI prompt). Its Id is a working unit when there is one.</summary>
    public async Task<Result<IReadOnlyList<ExerciseMachineOption>>> ListMachinesAsync(CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<ExerciseMachineOption>>.Success(MachineOptions(await _repository.ListMachinesAsync(cancellationToken)));

    /// <summary>Groups units by trimmed name, ignoring case. Public for tests.</summary>
    public static IReadOnlyList<ExerciseMachineOption> MachineOptions(IEnumerable<Equipment> machines) =>
        machines.GroupBy(e => e.EquipmentName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var working = g.Where(e => e.EquipmentStatus == EquipmentStatus.Operational).ToArray();
                return new ExerciseMachineOption((working.FirstOrDefault() ?? g.First()).Id, g.Key, g.Count(), working.Length);
            })
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<Result<ManagerExerciseResponse>> CreateAsync(
        ExerciseRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(request, null, cancellationToken) is { } error)
            return error;
        var exercise = new Exercise();
        Apply(exercise, request, await MachineAsync(request.EquipmentId, cancellationToken));
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            _repository.Add(exercise);
            await _repository.SaveChangesAsync(cancellationToken);
            _repository.Add(AuditedWriter.Audit(actorUserId, AuditActionType.Create, nameof(Exercise), ipAddress,
                newValue: $"Id={exercise.Id}; Name={exercise.Name}; Group={exercise.MuscleGroup}; Machine={exercise.Equipment?.EquipmentName}"));
            await _repository.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return Result<ManagerExerciseResponse>.Success(ToResponse(exercise, 0), 201);
    }

    public async Task<Result<ManagerExerciseResponse>> UpdateAsync(
        int id, ExerciseRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var exercise = await _repository.FindAsync(id, cancellationToken);
        if (exercise is null)
            return NotFound();
        if (await ValidateAsync(request, id, cancellationToken) is { } error)
            return error;
        var old = AuditText(exercise);
        Apply(exercise, request, await MachineAsync(request.EquipmentId, cancellationToken));
        exercise.UpdatedAt = DateTime.UtcNow;
        _repository.Add(AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Exercise), ipAddress, old, AuditText(exercise)));
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<ManagerExerciseResponse>.Success(ToResponse(exercise, await UsesAsync(id, cancellationToken)));
    }

    public async Task<Result<ManagerExerciseResponse>> SetActiveAsync(
        int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var exercise = await _repository.FindAsync(id, cancellationToken);
        if (exercise is null)
            return NotFound();
        if (exercise.IsActive != isActive)
        {
            exercise.IsActive = isActive;
            exercise.UpdatedAt = DateTime.UtcNow;
            _repository.Add(AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Exercise), ipAddress,
                $"Id={id}; IsActive={!isActive}", $"Id={id}; IsActive={isActive}"));
            await _repository.SaveChangesAsync(cancellationToken);
        }
        return Result<ManagerExerciseResponse>.Success(ToResponse(exercise, await UsesAsync(id, cancellationToken)));
    }

    /// <summary>Field rules for the catalog. Public for tests.</summary>
    public static string? FieldError(ExerciseRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            return "Tên bài tập bắt buộc và tối đa 100 ký tự.";
        if (!ExerciseCatalog.MuscleGroups.Contains(request.MuscleGroup))
            return "Hãy chọn nhóm cơ trong danh sách.";
        if (!ExerciseCatalog.Levels.Contains(request.DifficultyLevel))
            return "Hãy chọn độ khó: Cơ bản, Trung bình hoặc Nâng cao.";
        if (request.Description?.Trim().Length > 500)
            return "Mô tả tối đa 500 ký tự.";
        foreach (var url in new[] { request.ImageUrl, request.VideoUrl })
            if (!string.IsNullOrWhiteSpace(url) &&
                !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && url.Trim().Length <= 500))
                return "Link ảnh và video phải là địa chỉ http(s) hợp lệ, tối đa 500 ký tự.";
        return null;
    }

    private async Task<Result<ManagerExerciseResponse>?> ValidateAsync(ExerciseRequest request, int? id, CancellationToken cancellationToken)
    {
        if (FieldError(request) is { } message)
            return Result<ManagerExerciseResponse>.Failure(message, "EXERCISE_INVALID");
        if (await _repository.NameExistsAsync(request.Name.Trim(), id, cancellationToken))
            return Result<ManagerExerciseResponse>.Failure("Đã có bài tập cùng tên.", "EXERCISE_NAME_TAKEN", 409);
        if (request.EquipmentId is not null && await MachineAsync(request.EquipmentId, cancellationToken) is null)
            return Result<ManagerExerciseResponse>.Failure("Không tìm thấy máy tập đã chọn.", "EXERCISE_MACHINE_NOT_FOUND");
        return null;
    }

    private async Task<Equipment?> MachineAsync(int? equipmentId, CancellationToken cancellationToken) =>
        equipmentId is { } id ? (await _repository.ListMachinesAsync(cancellationToken)).FirstOrDefault(e => e.Id == id) : null;

    private static string AuditText(Exercise e) =>
        $"Name={e.Name}; Group={e.MuscleGroup}; Level={e.DifficultyLevel}; Machine={e.Equipment?.EquipmentName}";

    private static void Apply(Exercise exercise, ExerciseRequest request, Equipment? machine)
    {
        // A broken machine may still be picked: plans then use another working unit with the same name.
        exercise.EquipmentId = machine?.Id;
        exercise.Equipment = machine;   // tracked by the same context, so EF links it without inserting
        exercise.Name = request.Name.Trim();
        exercise.MuscleGroup = request.MuscleGroup;
        exercise.DifficultyLevel = request.DifficultyLevel;
        exercise.Description = Clean(request.Description);
        exercise.ImageUrl = Clean(request.ImageUrl);
        exercise.VideoUrl = Clean(request.VideoUrl);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<int> UsesAsync(int id, CancellationToken cancellationToken) =>
        (await _repository.ListAsync(cancellationToken)).FirstOrDefault(x => x.Exercise.Id == id).PlanUses;

    private static ManagerExerciseResponse ToResponse(Exercise e, int uses) =>
        new(e.Id, e.Name, e.MuscleGroup, e.DifficultyLevel, e.Description, e.ImageUrl, e.VideoUrl, e.IsActive, uses,
            e.EquipmentId, e.Equipment?.EquipmentName);

    private static Result<ManagerExerciseResponse> NotFound() =>
        Result<ManagerExerciseResponse>.Failure("Không tìm thấy bài tập.", "EXERCISE_NOT_FOUND", 404);
}
