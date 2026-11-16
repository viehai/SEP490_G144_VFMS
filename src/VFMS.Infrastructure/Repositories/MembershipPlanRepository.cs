using System.Data;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipPlanRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipPlanRepository
{
    public async Task<(IReadOnlyList<MembershipPlanReadModel> Items, int TotalCount)> SearchAsync(
        string? search, PackageStatus? status, int page, int pageSize, CancellationToken cancellationToken = default,
        ComponentType? serviceType = null)
    {
        var query = Plans(status);
        if (serviceType == ComponentType.GYM)
            query = query.Where(package => package.Components.Any(component => component.ComponentType == ComponentType.GYM));
        else if (serviceType == ComponentType.YOGA)   // a yoga plan has no GYM part; legacy gym+yoga combos stay gym plans
            query = query.Where(package => !package.Components.Any(component => component.ComponentType == ComponentType.GYM));
        if (search is not null)
            query = query.Where(package => package.Name.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query.OrderBy(package => package.Name).ThenBy(package => package.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<MembershipPlanReadModel?> GetDetailsAsync(
        int id, PackageStatus? requiredStatus, CancellationToken cancellationToken = default) =>
        Project(Plans(requiredStatus).Where(package => package.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public async Task<Package?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default)
    {
        // Acquire the package lock before inspecting components or usage. This serializes plan mutations
        // and protects the parent key from concurrent inserts that need to validate an OrderItem FK.
        var packages = Context.Database.IsSqlServer()
            ? Context.Packages.FromSqlInterpolated($"SELECT * FROM [Packages] WITH (XLOCK, HOLDLOCK) WHERE [Id] = {id}")
            : Context.Packages;
        var package = await packages.SingleOrDefaultAsync(package => package.Id == id, cancellationToken);
        if (package is null)
            return null;

        // Restrict FKs also apply to soft-deleted components, so load them for potential physical deletion.
        await Context.Entry(package).Collection(plan => plan.Components).Query()
            .IgnoreQueryFilters().LoadAsync(cancellationToken);
        return package.Components.Any(component => !component.IsDeleted &&
                component.ComponentType is ComponentType.GYM or ComponentType.YOGA)
            ? package
            : null;
    }

    public Task<bool> HasUsageAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Packages.IgnoreQueryFilters().AnyAsync(package => package.Id == id &&
            (package.OrderItems.Any() || package.Components.Any(component => component.MemberMemberships.Any())),
            cancellationToken);

    public void Remove(Package package)
    {
        Context.PackageComponents.RemoveRange(package.Components);
        Context.Packages.Remove(package);
    }

    public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await work();
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<Package> Plans(PackageStatus? status)
    {
        // Membership plans sell GYM or YOGA (PT packages are managed elsewhere).
        var plans = Context.Packages.AsNoTracking()
            .Where(package => package.Components.Any(component =>
                component.ComponentType == ComponentType.GYM || component.ComponentType == ComponentType.YOGA));
        return status.HasValue ? plans.Where(package => package.PackageStatus == status.Value) : plans;
    }

    // The main part is the GYM component, or the YOGA component of a yoga-only plan.
    private IQueryable<MembershipPlanReadModel> Project(IQueryable<Package> plans) =>
        plans.Select(package => new
            {
                package,
                Type = package.Components.Any(component => component.ComponentType == ComponentType.GYM)
                    ? ComponentType.GYM : ComponentType.YOGA
            })
            .Select(plan => new MembershipPlanReadModel(
                plan.package.Id,
                plan.package.Name,
                plan.package.Description,
                plan.package.Components.Where(component => component.ComponentType == plan.Type)
                    .OrderBy(component => component.Id).Select(component => component.DurationDays).FirstOrDefault(),
                plan.package.Components.Where(component => component.ComponentType == plan.Type)
                    .OrderBy(component => component.Id).Select(component => component.DurationMonths).FirstOrDefault(),
                plan.package.Price,
                plan.package.PackageStatus,
                Context.MemberMemberships.Count(membership => membership.MembershipStatus == MembershipStatus.Active &&
                    membership.PackageComponent.PackageId == plan.package.Id &&
                    membership.PackageComponent.ComponentType == plan.Type),
                plan.Type));
}
