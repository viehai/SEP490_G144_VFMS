using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Packages;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using MembershipDurationUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;

namespace VFMS.Application.Services;

public sealed class MembershipPlanService(IMembershipPlanRepository repository, TimeProvider clock) : IMembershipPlanService
{
    public async Task<Result<PagedResponse<MembershipPlanResponse>>> ListAsync(
        MembershipPlanListRequest request, CancellationToken cancellationToken)
    {
        PackageStatus? status = string.IsNullOrWhiteSpace(request.Status) ? null : ParseStatus(request.Status);
        var (items, total) = await repository.SearchAsync(
            Clean(request.Search), status, request.Page, request.PageSize, cancellationToken, ParseServiceType(request.ServiceType),
            durationUnit: request.DurationUnit, freezeAllowanceFilter: request.FreezeAllowanceFilter);
        return Result<PagedResponse<MembershipPlanResponse>>.Success(
            new(request.Page, request.PageSize, total, items.Select(ToResponse).ToArray()));
    }

    public async Task<Result<MembershipPlanResponse>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var plan = await repository.GetDetailsAsync(id, requiredStatus: null, cancellationToken);
        return plan is null ? NotFound<MembershipPlanResponse>() : Result<MembershipPlanResponse>.Success(ToResponse(plan));
    }

    public async Task<Result<PagedResponse<MembershipPlanCatalogItemResponse>>> ListCatalogAsync(
        MembershipPlanListRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Status) && ParseStatus(request.Status) == PackageStatus.Inactive)
            return Result<PagedResponse<MembershipPlanCatalogItemResponse>>.Success(
                new(request.Page, request.PageSize, 0, []));

        var (items, total) = await repository.SearchAsync(
            Clean(request.Search), PackageStatus.Active, request.Page, request.PageSize, cancellationToken,
            ParseServiceType(request.ServiceType), clock.GetUtcNow().UtcDateTime, request.DurationUnit);
        return Result<PagedResponse<MembershipPlanCatalogItemResponse>>.Success(
            new(request.Page, request.PageSize, total, items.Select(ToCatalogResponse).ToArray()));
    }

    public async Task<Result<MembershipPlanCatalogItemResponse>> GetCatalogItemAsync(
        int id, CancellationToken cancellationToken)
    {
        var plan = await repository.GetDetailsAsync(id, PackageStatus.Active, cancellationToken, clock.GetUtcNow().UtcDateTime);
        return plan is null
            ? NotFound<MembershipPlanCatalogItemResponse>()
            : Result<MembershipPlanCatalogItemResponse>.Success(ToCatalogResponse(plan));
    }

    public async Task<Result<MembershipPlanResponse>> CreateAsync(
        MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // A plan sells one service, GYM or YOGA (legacy combos keep GYM as their main part).
        var service = new PackageComponent { ComponentType = ParseServiceType(request.ServiceType) ?? ComponentType.GYM };
        if (await InvalidFreezeRangeAsync(service.ComponentType, request, cancellationToken))
            return InvalidFreezeRange();
        var plan = new Package { Components = [service] };
        Apply(plan, service, request);

        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.CreateMembershipPlan, nameof(Package), ipAddress,
            newValue: Snapshot(plan, service));
        await AuditedWriter.SaveNewAsync(repository, plan, audit, notifications: null, cancellationToken);
        return Result<MembershipPlanResponse>.Success(ToResponse(ReadCreatedPlan(plan, service)), 201);
    }

    public Task<Result<MembershipPlanResponse>> UpdateAsync(
        int id, MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken) =>
        MutateAsync(id, async plan =>
        {
            var gym = PrimaryComponent(plan);   // the type never changes on update
            if (await InvalidFreezeRangeAsync(gym.ComponentType, request, cancellationToken))
                return InvalidFreezeRange();
            var oldStatus = plan.PackageStatus;
            var before = Snapshot(plan, gym);
            Apply(plan, gym, request);
            var after = Snapshot(plan, gym);

            if (oldStatus != plan.PackageStatus)
            {
                var statusAudit = AuditedWriter.Audit(actorUserId, StatusAction(plan.PackageStatus), nameof(Package),
                    ipAddress, before, after);
                statusAudit.EntityId = plan.Id;
                repository.Add(statusAudit);
            }
            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.UpdateMembershipPlan, nameof(Package),
                ipAddress, before, after);
            await AuditedWriter.SaveChangedAsync(repository, plan, audit, notifications: null, cancellationToken);
            return await GetAsync(id, cancellationToken);
        }, cancellationToken);

    public Task<Result<MembershipPlanResponse>> ChangeStatusAsync(
        int id, MembershipPlanStatusRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken) =>
        MutateAsync(id, async plan =>
        {
            var status = ParseStatus(request.Status);
            if (plan.PackageStatus == status)
                return Result<MembershipPlanResponse>.Failure(
                    "Gói hội viên đã ở trạng thái này.", "MEMBERSHIP_PLAN_STATUS_UNCHANGED", 409);

            await SetStatusAsync(plan, status, actorUserId, ipAddress, cancellationToken);
            return await GetAsync(id, cancellationToken);
        }, cancellationToken);

    public Task<Result<MembershipPlanDeleteResponse>> DeleteAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken) =>
        MutateAsync(id, async plan =>
        {
            // Order lines and subscriptions retain their references, including historical or hidden rows.
            if (await repository.HasUsageAsync(id, cancellationToken))
            {
                if (plan.PackageStatus != PackageStatus.Inactive)
                    await SetStatusAsync(plan, PackageStatus.Inactive, actorUserId, ipAddress, cancellationToken);

                return Result<MembershipPlanDeleteResponse>.Success(new(true,
                    "Gói đã có đơn hàng hoặc người đăng ký nên không thể xóa vĩnh viễn. Gói đã chuyển sang ngừng bán."));
            }

            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.DeleteMembershipPlan, nameof(Package),
                ipAddress, oldValue: Snapshot(plan, PrimaryComponent(plan)));
            repository.Remove(plan);
            await AuditedWriter.SaveChangedAsync(repository, plan, audit, notifications: null, cancellationToken);
            return Result<MembershipPlanDeleteResponse>.Success(new(false, "Đã xóa vĩnh viễn gói hội viên."));
        }, cancellationToken);

    private async Task<Result<T>> MutateAsync<T>(
        int id, Func<Package, Task<Result<T>>> mutate, CancellationToken cancellationToken)
    {
        var result = NotFound<T>();
        await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            var plan = await repository.GetForUpdateAsync(id, cancellationToken);
            if (plan is not null)
                result = await mutate(plan);
        }, cancellationToken);
        return result;
    }

    private async Task SetStatusAsync(
        Package plan, PackageStatus status, string actorUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var gym = PrimaryComponent(plan);
        var before = Snapshot(plan, gym);
        plan.PackageStatus = status;
        var audit = AuditedWriter.Audit(actorUserId, StatusAction(status), nameof(Package), ipAddress, before, Snapshot(plan, gym));
        await AuditedWriter.SaveChangedAsync(repository, plan, audit, notifications: null, cancellationToken);
    }

    private async Task<bool> InvalidFreezeRangeAsync(ComponentType type, MembershipPlanRequest request, CancellationToken ct)
    {
        if (type != ComponentType.GYM || request.MaxFreezeCount == 0 ||
            request.MinFreezeDaysPerRequest.HasValue == request.MaxFreezeDaysPerRequest.HasValue)
            return false;
        var policy = await repository.GetCatalogFreezeRulesAsync(ct);
        return (request.MinFreezeDaysPerRequest ?? policy?.MinimumDays) > (request.MaxFreezeDaysPerRequest ?? policy?.MaximumDays);
    }

    private static Result<MembershipPlanResponse> InvalidFreezeRange() => Result<MembershipPlanResponse>.Failure(
        "Sau khi áp dụng chính sách chung, số ngày tối thiểu mỗi lượt vượt quá số ngày tối đa.", "FREEZE_DAYS_RANGE");

    private static void Apply(Package plan, PackageComponent gym, MembershipPlanRequest request)
    {
        if (gym.ComponentType == ComponentType.GYM)
        {
            gym.MaxFreezeCount = request.MaxFreezeCount;
            gym.MinFreezeDaysPerRequest = request.MinFreezeDaysPerRequest;
            gym.MaxFreezeDaysPerRequest = request.MaxFreezeDaysPerRequest;
        }
        plan.Name = request.Name.Trim();
        plan.Description = Clean(request.Description);
        plan.Price = request.Price!.Value;
        plan.PackageStatus = ParseStatus(request.Status);

        if (request.DurationValue is { } duration)
        {
            gym.DurationDays = request.DurationUnit == MembershipDurationUnit.Day ? duration : null;
            gym.DurationMonths = request.DurationUnit == MembershipDurationUnit.Month ? duration : null;
        }
        else if (request.DurationInDays != DisplayDays(gym.DurationDays, gym.DurationMonths))
        {
            // Preserve a calendar-month term when a day-only client round-trips its old day projection.
            gym.DurationDays = request.DurationInDays;
            gym.DurationMonths = null;
        }

        if (request.DurationValue.HasValue || request.TargetAudience is not null)
            plan.TargetAudience = Clean(request.TargetAudience);

        if (gym.ComponentType == ComponentType.YOGA && gym.DurationDays == 7)
        {
            plan.FreezeAllowanceOverrideDays = null;
            plan.FreezeAllowanceOverrideUnit = null;
        }
        else if (request.FreezeAllowanceOverrideValue is { } value &&
            MembershipFreezeDuration.TryToDays(value, request.FreezeAllowanceOverrideUnit, out var days))
        {
            plan.FreezeAllowanceOverrideDays = days;
            plan.FreezeAllowanceOverrideUnit = (VFMS.Domain.Enums.MembershipDurationUnit)request.FreezeAllowanceOverrideUnit;
        }
        else if (request.DurationValue.HasValue)
        {
            plan.FreezeAllowanceOverrideDays = null;
            plan.FreezeAllowanceOverrideUnit = null;
        }
    }

    /// <summary>The plan's main part: its GYM component, or for a yoga plan its YOGA component.</summary>
    private static PackageComponent PrimaryComponent(Package plan)
    {
        var live = plan.Components.Where(component => !component.IsDeleted).ToArray();
        var type = live.Any(component => component.ComponentType == ComponentType.GYM) ? ComponentType.GYM : ComponentType.YOGA;
        return live.Where(component => component.ComponentType == type).MinBy(component => component.Id)!;
    }

    private static MembershipPlanReadModel ReadCreatedPlan(Package plan, PackageComponent service) =>
        new(plan.Id, plan.Name, plan.Description, service.DurationDays, service.DurationMonths, plan.Price,
            plan.PackageStatus, SubscriberCount: 0, service.ComponentType)
        {
            MaxFreezeCount = service.MaxFreezeCount,
            MinFreezeDaysPerRequest = service.MinFreezeDaysPerRequest,
            MaxFreezeDaysPerRequest = service.MaxFreezeDaysPerRequest,
            TargetAudience = plan.TargetAudience,
            FreezeAllowanceOverrideDays = plan.FreezeAllowanceOverrideDays,
            FreezeAllowanceOverrideUnit = plan.FreezeAllowanceOverrideUnit
        };

    private static MembershipPlanResponse ToResponse(MembershipPlanReadModel plan) =>
        new(plan.Id, plan.Name, plan.Description, DisplayDays(plan.DurationDays, plan.DurationMonths), plan.Price,
            FormatStatus(plan.Status), plan.SubscriberCount, CalendarMonths(plan), plan.ServiceType.ToString())
        {
            MaxFreezeCount = plan.MaxFreezeCount,
            MinFreezeDaysPerRequest = plan.MinFreezeDaysPerRequest,
            MaxFreezeDaysPerRequest = plan.MaxFreezeDaysPerRequest,
            TargetAudience = plan.TargetAudience,
            FreezeAllowanceOverrideDays = plan.FreezeAllowanceOverrideDays,
            FreezeAllowanceOverrideUnit = (MembershipDurationUnit?)plan.FreezeAllowanceOverrideUnit ?? MembershipDurationUnit.Day
        };

    private static MembershipPlanCatalogItemResponse ToCatalogResponse(MembershipPlanReadModel plan) =>
        new(plan.Id, plan.Name, plan.Description, DisplayDays(plan.DurationDays, plan.DurationMonths), plan.Price,
            FormatStatus(plan.Status), CalendarMonths(plan), plan.ServiceType.ToString())
        {
            IsSingleService = plan.IsSingleService,
            TargetAudience = plan.TargetAudience
        };

    private static ComponentType? ParseServiceType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<ComponentType>(value.Trim(), ignoreCase: true);

    private static int? CalendarMonths(MembershipPlanReadModel plan) =>
        plan.DurationDays is not > 0 && plan.DurationMonths is > 0 ? plan.DurationMonths : null;

    private static int? DisplayDays(int? days, int? months) =>
        days is > 0 ? days : months is > 0 and <= int.MaxValue / 30 ? months * 30 : null;

    private static string Snapshot(Package plan, PackageComponent gym) => JsonSerializer.Serialize(new
    {
        ServiceType = gym.ComponentType.ToString(),
        plan.Name,
        plan.Description,
        plan.TargetAudience,
        DurationInDays = gym.DurationDays,
        DurationInMonths = gym.DurationMonths,
        plan.FreezeAllowanceOverrideDays,
        plan.FreezeAllowanceOverrideUnit,
        gym.MaxFreezeCount,
        gym.MinFreezeDaysPerRequest,
        gym.MaxFreezeDaysPerRequest,
        plan.Price,
        Status = FormatStatus(plan.PackageStatus)
    });

    private static AuditActionType StatusAction(PackageStatus status) =>
        status == PackageStatus.Active ? AuditActionType.ActivateMembershipPlan : AuditActionType.DeactivateMembershipPlan;

    private static PackageStatus ParseStatus(string value) => Enum.Parse<PackageStatus>(value.Trim(), ignoreCase: true);

    private static string FormatStatus(PackageStatus status) =>
        status == PackageStatus.Active ? MembershipPlanStatuses.Active : MembershipPlanStatuses.Inactive;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure("Không tìm thấy gói hội viên.", "MEMBERSHIP_PLAN_NOT_FOUND", 404);

    // ══════════════════════════════════════════════════════════
    // Public catalog: GET /api/packages?type=gym|yoga|pt
    // ══════════════════════════════════════════════════════════

    public async Task<Result<PagedResponse<PackageCatalogItemResponse>>> ListPackageCatalogAsync(
        PackageCatalogQuery request, CancellationToken cancellationToken)
    {
        if (!TryParseComponentType(request.Type, out var componentType))
            return Result<PagedResponse<PackageCatalogItemResponse>>.Failure(
                "Chọn loại gói Gym, Yoga hoặc PT.", "INVALID_COMPONENT_TYPE", 400);

        var (items, total) = await repository.SearchByComponentTypeAsync(
            componentType, request.Page, request.PageSize, cancellationToken);
        var freeze = items.Count > 0 && componentType != ComponentType.PT
            ? await repository.GetCatalogFreezeRulesAsync(cancellationToken) : null;

        return Result<PagedResponse<PackageCatalogItemResponse>>.Success(
            new(request.Page, request.PageSize, total,
                items.Select(item => ToCatalogItemResponse(item, freeze)).ToArray()));
    }

    public async Task<Result<PackageDetailResponse>> GetPackageDetailAsync(
        string type, int id, CancellationToken cancellationToken)
    {
        if (!TryParseComponentType(type, out var componentType))
            return Result<PackageDetailResponse>.Failure("Loại gói tập không hợp lệ.", "INVALID_COMPONENT_TYPE", 400);

        var packages = await repository.ListActiveSingleServicePackagesAsync(cancellationToken);
        var package = packages.SingleOrDefault(item => item.Id == id && item.ComponentType == componentType);
        if (package is null)
            return Result<PackageDetailResponse>.Failure("Không tìm thấy gói tập đang mở bán.", "PACKAGE_NOT_FOUND", 404);
        var freeze = packages.Any(item => item.ComponentType != ComponentType.PT)
            ? await repository.GetCatalogFreezeRulesAsync(cancellationToken) : null;

        var recommendations = packages.Where(item => item.Id != id)
            .GroupBy(item => item.ComponentType)
            .OrderBy(group => group.Key == componentType ? -1 : (int)group.Key)
            .Select(group => group.Key == componentType
                ? group.OrderBy(item => RecommendationDistance(package, item))
                    .ThenBy(item => decimal.Abs(item.Price - package.Price))
                    .ThenBy(item => item.Id).First()
                : group.OrderBy(item => item.Price).ThenBy(item => item.Id).First())
            .Select(item => ToCatalogItemResponse(item, freeze))
            .ToArray();

        return Result<PackageDetailResponse>.Success(new(ToCatalogItemResponse(package, freeze), recommendations));
    }

    private static long RecommendationDistance(PackageCatalogReadModel reference, PackageCatalogReadModel candidate)
    {
        if (reference.ComponentType == ComponentType.PT)
            return Distance(reference.SessionCount, candidate.SessionCount);

        return Distance(ComparableDuration(reference), ComparableDuration(candidate));
    }

    private static long Distance(long? reference, long? candidate) =>
        reference is { } left && candidate is { } right ? Math.Abs(left - right) : long.MaxValue;

    // Convert months to comparable days only for ranking catalog cards.
    private static long? ComparableDuration(PackageCatalogReadModel package) =>
        package.DurationDays is > 0 ? package.DurationDays.Value :
        package.DurationMonths is > 0 ? 30L * package.DurationMonths.Value : null;

    private static bool TryParseComponentType(string? type, out ComponentType result)
    {
        result = default;
        var name = type?.Trim();
        return Enum.TryParse(name, ignoreCase: true, out result) && Enum.IsDefined(result) &&
            string.Equals(name, result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static PackageCatalogItemResponse ToCatalogItemResponse(PackageCatalogReadModel m, CatalogMembershipFreezeRules? freeze)
    {
        var allowance = MembershipPurchaseService.ResolveFreezeAllowance(m.ComponentType, m.DurationDays,
            m.FreezeAllowanceOverrideDays);
        var allowed = freeze is { MinimumDays: > 0 } && freeze.MaximumDays >= freeze.MinimumDays && allowance >= freeze.MinimumDays;
        var isGym = m.ComponentType == ComponentType.GYM;
        var count = m.MaxFreezeCount ?? freeze?.DefaultMaxFreezeCount;
        var maxDays = m.MaxFreezeDaysPerRequest ?? freeze?.MaximumDays;
        var minDays = m.MinFreezeDaysPerRequest ?? freeze?.MinimumDays;
        if (isGym)
            allowed = freeze is not null && count > 0 && minDays > 0 && maxDays >= minDays;
        var isPt = m.ComponentType == ComponentType.PT;
        return new(
            m.Id,
            m.Name,
            m.Description,
            m.Price,
            m.Status == PackageStatus.Active ? MembershipPlanStatuses.Active : MembershipPlanStatuses.Inactive,
            m.ComponentType.ToString().ToUpperInvariant(),
            m.DurationDays,
            m.DurationMonths,
            m.SessionCount,
            m.YogaClassTypeId,
            m.YogaClassTypeName,
            m.ApplicableTrainerLevel,
            m.PTFreezeMinimumDays,
            m.PTFreezeMaxTotalDays,
            isPt ? m.AllowFreeze : allowed,
            isGym ? maxDays : isPt ? m.MaxFreezeDaysPerRequest : allowed ? Math.Min(allowance!.Value, freeze!.MaximumDays) : null,
            isGym ? count : isPt ? m.MaxFreezeCount : null,
            m.AccessStartTime,
            m.AccessEndTime)
        {
            IsSingleService = m.IsSingleService,
            TargetAudience = m.TargetAudience,
            TotalFreezeAllowanceDays = isGym ? null : allowance
        };
    }
}
