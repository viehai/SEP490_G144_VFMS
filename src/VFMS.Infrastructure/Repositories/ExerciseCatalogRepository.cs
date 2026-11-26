using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class ExerciseCatalogRepository : AuditedRepositoryBase, IExerciseCatalogRepository
{
    public ExerciseCatalogRepository(VfmsDbContext context) : base(context) { }

    public async Task<IReadOnlyList<(Exercise Exercise, int PlanUses)>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Context.Exercises
            .OrderByDescending(e => e.IsActive).ThenBy(e => e.MuscleGroup).ThenBy(e => e.Name)
            .Select(e => new { Exercise = e, Uses = e.PlanItems.Count(i => !i.WorkoutPlan.IsTemplate) })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Exercise, r.Uses)).ToArray();
    }

    public Task<Exercise?> FindAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Exercises.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken = default) =>
        Context.Exercises.AnyAsync(e => e.Name == name && e.Id != exceptId, cancellationToken);
}
