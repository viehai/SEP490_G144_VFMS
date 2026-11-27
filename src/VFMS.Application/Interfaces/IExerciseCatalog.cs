using VFMS.Contracts.Common;
using VFMS.Contracts.Workout;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

/// <summary>Manager's exercise catalog (the exercises the AI and "Đổi bài tập" choose from).</summary>
public interface IExerciseCatalogRepository : IAuditedRepository
{
    /// <summary>Every exercise, active or not, with how many plan items use it.</summary>
    Task<IReadOnlyList<(Exercise Exercise, int PlanUses)>> ListAsync(CancellationToken cancellationToken = default);
    Task<Exercise?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken = default);
    /// <summary>Every machine (any status), for the exercise's default-machine picker.</summary>
    Task<IReadOnlyList<Equipment>> ListMachinesAsync(CancellationToken cancellationToken = default);
}

public interface IExerciseCatalogService
{
    Task<Result<IReadOnlyList<ManagerExerciseResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ExerciseMachineOption>>> ListMachinesAsync(CancellationToken cancellationToken = default);
    Task<Result<ManagerExerciseResponse>> CreateAsync(ExerciseRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<ManagerExerciseResponse>> UpdateAsync(int id, ExerciseRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<ManagerExerciseResponse>> SetActiveAsync(int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
