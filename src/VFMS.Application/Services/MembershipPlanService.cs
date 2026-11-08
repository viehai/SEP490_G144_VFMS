using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipPlanService(IMembershipPlanRepository repository) : IMembershipPlanService
{
    public async Task<Result<PagedResponse<MembershipPlanResponse>>> ListAsync(
        MembershipPlanListRequest request, CancellationToken cancellationToken)
    {
        PackageStatus? status = string.IsNullOrWhiteSpace(request.Status) ? null : ParseStatus(request.Status);
        var (items, total) = await repository.SearchAsync(
            Clean(request.Search), status, request.Page, request.PageSize, cancellationToken);
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
            Clean(request.Search), PackageStatus.Active, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<MembershipPlanCatalogItemResponse>>.Success(
            new(request.Page, request.PageSize, total, items.Select(ToCatalogResponse).ToArray()));
    }

    public async Task<Result<MembershipPlanCatalogItemResponse>> GetCatalogItemAsync(
        int id, CancellationToken cancellationToken)
    {
        var plan = await repository.GetDetailsAsync(id, PackageStatus.Active, cancellationToken);
        return plan is null
            ? NotFound<MembershipPlanCatalogItemResponse>()
            : Result<MembershipPlanCatalogItemResponse>.Success(ToCatalogResponse(plan));
    }

    public async Task<Result<MembershipPlanResponse>> CreateAsync(
        MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var gym = new PackageComponent { ComponentType = ComponentType.GYM };
        var plan = new Package { Components = [gym] };
        Apply(plan, gym, request);

        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.CreateMembershipPlan, nameof(Package), ipAddress,
            newValue: Snapshot(plan, gym));
        await AuditedWriter.SaveNewAsync(repository, plan, audit, notifications: null, cancellationToken);
        return Result<MembershipPlanResponse>.Success(ToResponse(ReadCreatedPlan(plan, gym)), 201);
    }

    public Task<Result<MembershipPlanResponse>> UpdateAsync(
        int id, MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken) =>
        MutateAsync(id, async plan =>
        {
            var gym = PrimaryGymComponent(plan);
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
                    "The membership plan already has this status.", "MEMBERSHIP_PLAN_STATUS_UNCHANGED", 409);

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
                    "This membership plan has order or subscription records and cannot be permanently deleted. It is inactive."));
            }

            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.DeleteMembershipPlan, nameof(Package),
                ipAddress, oldValue: Snapshot(plan, PrimaryGymComponent(plan)));
            repository.Remove(plan);
            await AuditedWriter.SaveChangedAsync(repository, plan, audit, notifications: null, cancellationToken);
            return Result<MembershipPlanDeleteResponse>.Success(new(false, "The membership plan was permanently deleted."));
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
        var gym = PrimaryGymComponent(plan);
        var before = Snapshot(plan, gym);
        plan.PackageStatus = status;
        var audit = AuditedWriter.Audit(actorUserId, StatusAction(status), nameof(Package), ipAddress, before, Snapshot(plan, gym));
        await AuditedWriter.SaveChangedAsync(repository, plan, audit, notifications: null, cancellationToken);
    }

    private static void Apply(Package plan, PackageComponent gym, MembershipPlanRequest request)
    {
        plan.Name = request.Name.Trim();
        plan.Description = Clean(request.Description);
        plan.Price = request.Price!.Value;
        plan.PackageStatus = ParseStatus(request.Status);

        // Keep calendar-month terms on legacy packages unless the manager explicitly changes the duration.
        if (request.DurationInDays != DisplayDays(gym.DurationDays, gym.DurationMonths))
        {
            gym.DurationDays = request.DurationInDays;
            gym.DurationMonths = null;
        }
    }

    private static PackageComponent PrimaryGymComponent(Package plan) => plan.Components
        .Where(component => !component.IsDeleted && component.ComponentType == ComponentType.GYM)
        .MinBy(component => component.Id)!;

    private static MembershipPlanReadModel ReadCreatedPlan(Package plan, PackageComponent gym) =>
        new(plan.Id, plan.Name, plan.Description, gym.DurationDays, gym.DurationMonths, plan.Price,
            plan.PackageStatus, SubscriberCount: 0);

    private static MembershipPlanResponse ToResponse(MembershipPlanReadModel plan) =>
        new(plan.Id, plan.Name, plan.Description, DisplayDays(plan.DurationDays, plan.DurationMonths), plan.Price,
            FormatStatus(plan.Status), plan.SubscriberCount, LegacyMonths(plan));

    private static MembershipPlanCatalogItemResponse ToCatalogResponse(MembershipPlanReadModel plan) =>
        new(plan.Id, plan.Name, plan.Description, DisplayDays(plan.DurationDays, plan.DurationMonths), plan.Price,
            FormatStatus(plan.Status), LegacyMonths(plan));

    private static int? LegacyMonths(MembershipPlanReadModel plan) =>
        plan.DurationDays is not > 0 && plan.DurationMonths is > 0 ? plan.DurationMonths : null;

    private static int? DisplayDays(int? days, int? months) =>
        days is > 0 ? days : months is > 0 and <= int.MaxValue / 30 ? months * 30 : null;

    private static string Snapshot(Package plan, PackageComponent gym) => JsonSerializer.Serialize(new
    {
        plan.Name,
        plan.Description,
        DurationInDays = gym.DurationDays,
        DurationInMonths = gym.DurationMonths,
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
        Result<T>.Failure("Membership plan was not found.", "MEMBERSHIP_PLAN_NOT_FOUND", 404);
}
