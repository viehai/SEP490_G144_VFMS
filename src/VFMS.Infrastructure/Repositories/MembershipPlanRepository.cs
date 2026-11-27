using System.Data;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using MembershipDurationUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipPlanRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipPlanRepository
{
    public async Task<(IReadOnlyList<MembershipPlanReadModel> Items, int TotalCount)> SearchAsync(
        string? search, PackageStatus? status, int page, int pageSize, CancellationToken cancellationToken = default,
        ComponentType? serviceType = null, DateTime? sellableAtUtc = null,
        MembershipDurationUnit? durationUnit = null, MembershipFreezeAllowanceFilter? freezeAllowanceFilter = null)
    {
        var query = Plans(status, sellableAtUtc);
        if (serviceType == ComponentType.GYM)
            query = query.Where(package => package.Components.Any(component => component.ComponentType == ComponentType.GYM));
        else if (serviceType == ComponentType.YOGA)   // a yoga plan has no GYM part; legacy gym+yoga combos stay gym plans
            query = query.Where(package => !package.Components.Any(component => component.ComponentType == ComponentType.GYM));
        if (search is not null)
            query = query.Where(package => package.Name.Contains(search));
        if (durationUnit is { } unit)
            query = query.Where(package => package.Components.Where(component =>
                    component.ComponentType == ComponentType.GYM || component.ComponentType == ComponentType.YOGA)
                .OrderByDescending(component => component.ComponentType == ComponentType.GYM).ThenBy(component => component.Id)
                .Select(component => unit == MembershipDurationUnit.Day
                    ? component.DurationDays > 0 : (component.DurationDays == null || component.DurationDays <= 0) && component.DurationMonths > 0)
                .FirstOrDefault());
        query = freezeAllowanceFilter switch
        {
            MembershipFreezeAllowanceFilter.Default => query.Where(p => p.Components.Any(c => c.ComponentType == ComponentType.GYM) &&
                !p.Components.Any(c => c.ComponentType == ComponentType.GYM && (c.MaxFreezeCount != null || c.MinFreezeDaysPerRequest != null || c.MaxFreezeDaysPerRequest != null))),
            MembershipFreezeAllowanceFilter.Disabled => query.Where(p => !p.Components.Any(c => c.ComponentType == ComponentType.GYM) ||
                p.Components.Any(c => c.ComponentType == ComponentType.GYM && c.MaxFreezeCount == 0)),
            MembershipFreezeAllowanceFilter.Custom => query.Where(p => p.Components.Any(c => c.ComponentType == ComponentType.GYM &&
                (c.MaxFreezeCount == null || c.MaxFreezeCount > 0) &&
                (c.MaxFreezeCount > 0 || c.MinFreezeDaysPerRequest != null || c.MaxFreezeDaysPerRequest != null))),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query.OrderByDescending(package => package.PackageStatus == PackageStatus.Active)
            .ThenBy(package => package.Name).ThenBy(package => package.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<MembershipPlanReadModel?> GetDetailsAsync(
        int id, PackageStatus? requiredStatus, CancellationToken cancellationToken = default, DateTime? sellableAtUtc = null) =>
        Project(Plans(requiredStatus, sellableAtUtc).Where(package => package.Id == id)).SingleOrDefaultAsync(cancellationToken);

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

    /// <summary>
    /// Public catalog query: Package Active có ít nhất 1 component không bị xóa mềm
    /// có ComponentType khớp với <paramref name="componentType"/>.
    /// Component được project theo type đã yêu cầu (first match, ordered by Id).
    /// </summary>
    public async Task<(IReadOnlyList<PackageCatalogReadModel> Items, int TotalCount)> SearchByComponentTypeAsync(
        ComponentType componentType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.Packages.AsNoTracking()
            .Where(p => p.PackageStatus == PackageStatus.Active
                     && p.Components.Any(c => c.ComponentType == componentType
                                           && (c.ComponentType != ComponentType.YOGA || c.YogaDifficultyLevel != null)));   // yoga plans without a level are not sold
        var total = await query.CountAsync(cancellationToken);
        var packages = await query.Include(p => p.Components)
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = packages.Select(package => ToCatalogItem(package, package.Components
            .OrderBy(component => component.Id).First(component => component.ComponentType == componentType))).ToArray();
        return (items, total);
    }

    public async Task<IReadOnlyList<PackageCatalogReadModel>> ListActiveSingleServicePackagesAsync(CancellationToken cancellationToken)
    {
        var packages = await Context.Packages.AsNoTracking()
            .Where(package => package.PackageStatus == PackageStatus.Active && package.Components.Count == 1)
            .Include(package => package.Components)
            .ToListAsync(cancellationToken);

        return packages.Select(package => ToCatalogItem(package, package.Components.Single())).ToArray();
    }

    public Task<CatalogMembershipFreezeRules?> GetCatalogFreezeRulesAsync(CancellationToken cancellationToken) =>
        Context.FreezePolicies.AsNoTracking().Where(policy => policy.IsGlobal && policy.PolicyStatus == PolicyStatus.Active)
            .Select(policy => new CatalogMembershipFreezeRules(policy.MinFreezeDaysPerRequest,
                policy.DefaultMaxFreezeDays, policy.DefaultMaxFreezeCount))
            .SingleOrDefaultAsync(cancellationToken);

    private static PackageCatalogReadModel ToCatalogItem(Package package, PackageComponent component) =>
        new(
            package.Id,
            package.Name,
            package.Description,
            package.Price,
            package.PackageStatus,
            component.ComponentType,
            component.DurationDays,
            component.DurationMonths,
            component.SessionCount,
            component.YogaDifficultyLevel,
            component.ApplicableTrainerLevel?.ToString(),
            component.ComponentType == ComponentType.PT ? component.MinFreezeDaysPerRequest : null,
            component.ComponentType == ComponentType.PT ? component.MaxFreezeDaysPerRequest : null,
            component.AllowFreeze,
            component.MaxFreezeDaysPerRequest,
            component.MaxFreezeCount,
            component.AccessStartTime,
            component.AccessEndTime)
        {
            MinFreezeDaysPerRequest = component.MinFreezeDaysPerRequest,
            IsSingleService = package.Components.Count == 1,
            TargetAudience = package.TargetAudience
        };

    private IQueryable<Package> Plans(PackageStatus? status, DateTime? sellableAtUtc = null)
    {
        // Membership plans sell GYM or YOGA (PT packages are managed elsewhere).
        var plans = Context.Packages.AsNoTracking()
            .Where(package => package.Components.Any(component =>
                component.ComponentType == ComponentType.GYM || component.ComponentType == ComponentType.YOGA));
        if (sellableAtUtc is { } now)
        {
            // A yoga plan sells only with a level and a bookable class it covers (higher levels include lower ones).
            var bookable = Context.YogaClasses.Where(YogaClassRules.Bookable(now));
            plans = plans.Where(package =>
                package.Components.Any(component => component.ComponentType == ComponentType.GYM) ||
                package.Components.Any(component => component.ComponentType == ComponentType.YOGA && (
                    (component.YogaDifficultyLevel == "Advanced" && bookable.Any()) ||
                    (component.YogaDifficultyLevel == "Intermediate" &&
                        bookable.Any(c => c.DifficultyLevel == "Beginner" || c.DifficultyLevel == "Intermediate")) ||
                    (component.YogaDifficultyLevel == "Beginner" && bookable.Any(c => c.DifficultyLevel == "Beginner")))));
        }
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
                plan.Type,
                plan.package.Components.Where(component => component.ComponentType == plan.Type)
                    .OrderBy(component => component.Id).Select(component => component.YogaDifficultyLevel).FirstOrDefault())
            {
                IsSingleService = plan.package.Components.Count == 1,
                MaxFreezeCount = plan.package.Components.Where(c => c.ComponentType == plan.Type).OrderBy(c => c.Id).Select(c => c.MaxFreezeCount).FirstOrDefault(),
                MinFreezeDaysPerRequest = plan.package.Components.Where(c => c.ComponentType == plan.Type).OrderBy(c => c.Id).Select(c => c.MinFreezeDaysPerRequest).FirstOrDefault(),
                MaxFreezeDaysPerRequest = plan.package.Components.Where(c => c.ComponentType == plan.Type).OrderBy(c => c.Id).Select(c => c.MaxFreezeDaysPerRequest).FirstOrDefault(),
                TargetAudience = plan.package.TargetAudience
            });
}
