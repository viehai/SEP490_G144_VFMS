using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class WorkoutRepository : AuditedRepositoryBase, IWorkoutRepository
{
    public WorkoutRepository(VfmsDbContext context) : base(context) { }

    public Task<PhysicalProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.PhysicalProfiles.FirstOrDefaultAsync(p => p.MemberUserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Exercise>> GetActiveExercisesAsync(CancellationToken cancellationToken = default) =>
        await Context.Exercises.Where(e => e.IsActive).OrderBy(e => e.MuscleGroup).ThenBy(e => e.Name).ToListAsync(cancellationToken);

    public Task<WorkoutPlan?> GetFallbackTemplateAsync(string fitnessGoal, CancellationToken cancellationToken = default) =>
        Context.WorkoutPlans.AsNoTracking()
            .Include(p => p.PlanItems).ThenInclude(i => i.Exercise)
            .Where(p => p.IsTemplate && p.FitnessGoal == fitnessGoal)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkoutPlan>> GetActivePlansAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.WorkoutPlans
            .Where(p => p.MemberUserId == userId && !p.IsTemplate && p.PlanStatus == WorkoutPlanStatus.Active)
            .ToListAsync(cancellationToken);

    public Task<WorkoutPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default) =>
        Context.WorkoutPlans
            .Include(p => p.PlanItems).ThenInclude(i => i.Exercise)
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

    public async Task<IReadOnlyList<WorkoutPlan>> GetPlanHistoryAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.WorkoutPlans.AsNoTracking()
            .Include(p => p.PlanItems)
            .Where(p => p.MemberUserId == userId && !p.IsTemplate)
            .OrderByDescending(p => p.GeneratedAt)
            .ToListAsync(cancellationToken);
}
