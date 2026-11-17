using System.Text.Json;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    public async Task<Result<IReadOnlyList<PTWeeklySlot>>> WeeklyTemplate(string trainerId, CancellationToken ct)
    {
        if (await User(trainerId, UserRole.PT, ct) is null)
            return Missing<IReadOnlyList<PTWeeklySlot>>();

        var slots = await Templates(trainerId).AsNoTracking(store).OrderBy(s => s.StartTime).ThenBy(s => s.DayOfWeek)
            .Select(s => new PTWeeklySlot(s.Id, s.DayOfWeek!.Value, s.StartTime, s.EndTime, null)).ToArrayAsync(store, ct);
        return Result<IReadOnlyList<PTWeeklySlot>>.Success(slots);
    }

    public async Task<Result<IReadOnlyList<PTCalendarSlot>>> Calendar(string trainerId, DateOnly from, CancellationToken ct)
    {
        if (from.DayNumber > DateOnly.MaxValue.DayNumber - 13)
            return Invalid<IReadOnlyList<PTCalendarSlot>>("Ngày xem lịch không hợp lệ.");
        var trainer = await User(trainerId, UserRole.PT, ct);
        if (trainer is null)
            return Missing<IReadOnlyList<PTCalendarSlot>>();
        var through = from.AddDays(13);
        var templates = await Templates(trainerId).AsNoTracking(store).ToListAsync(store, ct);
        var booked = await Occupied(Now).Where(s => s.PTUserId == trainerId &&
            s.SessionDate >= from && s.SessionDate <= through).ToListAsync(store, ct);
        var yoga = await Yoga(trainerId, null, from, through).ToListAsync(store, ct);
        var slots = PersonalTrainingRules.Occurrences(from, through, templates, int.MaxValue)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.EndTime,
                booked.Any(s => s.SessionDate == x.Date && PersonalTrainingRules.Overlaps(x.Slot.StartTime, x.Slot.EndTime, s.StartTime, s.EndTime)) ||
                yoga.Any(s => s.ScheduledDate == x.Date && PersonalTrainingRules.Overlaps(x.Slot.StartTime, x.Slot.EndTime, s.StartTime, s.EndTime))
                    ? "Booked" : Utc(x.Date, x.Slot.StartTime) <= Now ? "Past" : "Available")).ToList();
        slots.AddRange(booked.Where(s => !slots.Any(x => x.Date == s.SessionDate &&
                x.StartTime == s.StartTime && x.EndTime == s.EndTime))
            .Select(s => new PTCalendarSlot(s.PTAvailabilityId ?? 0, s.SessionDate, s.StartTime, s.EndTime, "Booked"))
            .ToArray());
        return Result<IReadOnlyList<PTCalendarSlot>>.Success(slots.OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToArray());
    }

    public async Task<Result<IReadOnlyList<PTSlotInput>>> Template(string actor, CancellationToken ct)
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<IReadOnlyList<PTSlotInput>>();
        var slots = await Templates(actor).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new PTSlotInput(x.DayOfWeek!.Value, x.StartTime)).ToListAsync(store, ct);
        return Result<IReadOnlyList<PTSlotInput>>.Success(slots);
    }

    public async Task<Result<PTOperatingHours>> OperatingHours(string actor, CancellationToken ct)
    {
        var trainer = await User(actor, UserRole.PT, ct);
        return trainer is null
            ? Forbidden<PTOperatingHours>()
            : Result<PTOperatingHours>.Success(new(trainer.Gym.OpenTime, trainer.Gym.CloseTime));
    }

    public Task<Result<bool>> Publish(string actor, PTPublishRequest request, CancellationToken ct) => Write(async () =>
    {
        var trainer = await User(actor, UserRole.PT, ct);
        if (trainer is null)
            return Forbidden<bool>();
        if (await ActiveYogaAssignments.AnyAsync(store, c => c.InstructorUserId == actor, ct))
            return Conflict<bool>("Huấn luyện viên đang phụ trách lớp Yoga.");
        if (request.Slots.Any(s => s is null))
            return Invalid<bool>("Mẫu lịch chứa ca không hợp lệ.");
        var desired = request.Slots.Distinct().ToArray();
        if (desired.Length != request.Slots.Length ||
            desired.Any(s => !Enum.IsDefined(s.DayOfWeek) || !InHours(trainer.Gym, s.StartTime, s.StartTime.AddHours(1))))
            return Invalid<bool>("Ca phải hợp lệ, không trùng nhau và nằm trong giờ hoạt động của gym.");
        if (desired.GroupBy(s => s.DayOfWeek).Any(group => group.OrderBy(s => s.StartTime).Zip(
                group.OrderBy(s => s.StartTime).Skip(1), (left, right) =>
                    right.StartTime - left.StartTime < TimeSpan.FromHours(1)).Any(x => x)))
            return Invalid<bool>("Các ca trong cùng ngày không được chồng giờ.");

        var existing = await Templates(actor).ToListAsync(store, ct);
        var added = desired.Where(slot => !existing.Any(x => x.DayOfWeek == slot.DayOfWeek && x.StartTime == slot.StartTime)).ToArray();
        if (added.Any(slot => !IsWholeHour(slot.StartTime)))
            return Invalid<bool>("Ca mới phải bắt đầu vào giờ tròn, ví dụ 06:00 hoặc 07:00.");
        var removed = existing.Where(x => !desired.Contains(new(x.DayOfWeek!.Value, x.StartTime))).ToArray();
        var removedIds = removed.Select(x => x.Id).ToArray();
        var future = await Occupied(Now).Where(s => s.PTUserId == actor && s.SessionDate >= Today).ToListAsync(store, ct);
        if (future.Any(s => s.PTAvailabilityId is { } id && removedIds.Contains(id) ||
            removed.Any(slot => slot.DayOfWeek == s.SessionDate.DayOfWeek &&
                slot.StartTime < s.EndTime && s.StartTime < slot.EndTime)))
            return Conflict<bool>("Không thể sửa hoặc tắt ca đang được giữ hoặc đã xác nhận.");
        var yoga = await Yoga(actor, null, Today, DateOnly.MaxValue).ToListAsync(store, ct);
        if (added.Any(slot => future.Any(s => s.SessionDate.DayOfWeek == slot.DayOfWeek &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.StartTime.AddHours(1), s.StartTime, s.EndTime)) ||
            yoga.Any(s => s.ScheduledDate.DayOfWeek == slot.DayOfWeek &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.StartTime.AddHours(1), s.StartTime, s.EndTime))))
            return Conflict<bool>("Ca lặp trùng lịch đã đặt hoặc lớp Yoga đã phân công.");

        foreach (var slot in removed)
            slot.IsDeleted = true;
        store.AddRange(added.Select(s => new PTAvailability
        {
            PTUserId = actor,
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            EndTime = s.StartTime.AddHours(1)
        }));
        await store.Save(ct);
        return Result<bool>.Success(true);
    }, ct);

    private IQueryable<Package> PTPackages => store.Query<Package>().Including(store, "Components")
        .Where(p => p.Components.Count == 1 &&
            p.Components.Any(c => c.ComponentType == ComponentType.PT && c.ApplicableTrainerLevel != null));

    public async Task<Result<IReadOnlyList<PTPackageResponse>>> Packages(bool all, CancellationToken ct)
    {
        var packages = await PTPackages.AsNoTracking(store).Where(p => all || p.PackageStatus == PackageStatus.Active)
            .OrderBy(p => p.Id).ToListAsync(store, ct);
        if (!all)
            return Result<IReadOnlyList<PTPackageResponse>>.Success(packages.Select(p => PackageResponse(p)).ToArray());

        var today = Today;
        var registrations = (await (
            from purchase in store.Query<PTPurchase>()
            join item in store.Query<OrderItem>() on purchase.OrderId equals (int?)item.OrderId
            where !purchase.IsCompleted && purchase.EndDate >= today &&
                (purchase.Status == PTPurchaseStatus.Active || purchase.Status == PTPurchaseStatus.Frozen)
            select new { item.PackageId, PurchaseId = purchase.Id })
            .Distinct().GroupBy(x => x.PackageId)
            .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(store, ct))
            .ToDictionary(group => group.Key, group => group.Count);
        return Result<IReadOnlyList<PTPackageResponse>>.Success(
            packages.Select(p => PackageResponse(p, registrations.GetValueOrDefault(p.Id))).ToArray());
    }

    public async Task<Result<IReadOnlyList<PTPackageResponse>>> ManagerPackages(string actor, CancellationToken ct) =>
        await User(actor, UserRole.Manager, ct) is null ? Forbidden<IReadOnlyList<PTPackageResponse>>() : await Packages(true, ct);

    private static PTPackageResponse PackageResponse(Package p, int? subscriberCount = null)
    {
        var component = p.Components.Single();
        return new(p.Id, $"PT-{p.Id:D4}", p.Name, p.Description, component.SessionCount!.Value,
            p.Price, component.ApplicableTrainerLevel!.Value.ToString(),
            component.PTFreezeMinimumDays ?? 0, component.PTFreezeMaxTotalDays ?? 0,
            p.PackageStatus == PackageStatus.Active, subscriberCount);
    }

    public Task<Result<bool>> SetPackageSaleStatus(string actor, int id, bool isActive, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Manager, ct) is null)
                return Forbidden<bool>();
            var package = await PTPackages.SingleOrDefaultAsync(store, p => p.Id == id, ct);
            if (package is null)
                return Missing<bool>();

            SetPackageStatus(package, isActive ? PackageStatus.Active : PackageStatus.Inactive, actor);
            await store.Save(ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<PTPackageDeleteResponse>> DeletePackage(string actor, int id, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Manager, ct) is null)
                return Forbidden<PTPackageDeleteResponse>();
            var package = await PTPackages.SingleOrDefaultAsync(store, p => p.Id == id, ct);
            if (package is null)
                return Missing<PTPackageDeleteResponse>();

            // Hidden references still belong to the order and subscription history.
            var hasUsage = await store.PackageHasHistory(id, ct);
            if (hasUsage)
                SetPackageStatus(package, PackageStatus.Inactive, actor);
            else
            {
                store.RemoveRange(package.Components);
                store.Remove(package);
                store.Add(new AuditLog
                {
                    ActorUserId = actor,
                    ActionType = AuditActionType.Delete,
                    AuditEntity = nameof(Package),
                    EntityId = id,
                    OldValue = JsonSerializer.Serialize(PackageResponse(package)),
                    OccurredAt = Now
                });
            }
            await store.Save(ct);
            return Result<PTPackageDeleteResponse>.Success(new(hasUsage));
        }, ct);

    private void SetPackageStatus(Package package, PackageStatus status, string actor)
    {
        if (package.PackageStatus == status)
            return;
        package.PackageStatus = status;
        Audit(actor, nameof(Package), package.Id, JsonSerializer.Serialize(new { Status = status.ToString() }));
    }

    public Task<Result<PTPackageResponse>> SavePackage(string actor, int? id, PTPackageRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Manager, ct) is null)
                return Forbidden<PTPackageResponse>();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Credits <= 0 ||
                request.Price <= 0 ||
                request.Price > 9_999_999_999_999_999m || decimal.Truncate(request.Price) != request.Price ||
                !Enum.TryParse<TrainerLevel>(request.TrainerLevel, true, out var level) ||
                !string.Equals(request.TrainerLevel, level.ToString(), StringComparison.OrdinalIgnoreCase))
                return Invalid<PTPackageResponse>("Tên, số buổi, giá hoặc cấp PT không hợp lệ.");
            if (request.FreezeMinimumDays < 1 || request.FreezeMaxTotalDays < request.FreezeMinimumDays)
                return Invalid<PTPackageResponse>("Số ngày bảo lưu tối thiểu phải dương và không vượt tổng hạn mức.");
            var package = id is { } packageId ? await PTPackages.SingleOrDefaultAsync(store, p => p.Id == packageId, ct) :
                new Package { Components = [new PackageComponent { ComponentType = ComponentType.PT }] };
            if (package is null)
                return Missing<PTPackageResponse>();
            package.Name = request.Name.Trim();
            package.Description = request.Description?.Trim();
            package.Price = request.Price;
            var status = request.IsActive ? PackageStatus.Active : PackageStatus.Inactive;
            if (id is null)
                package.PackageStatus = status;
            else
                SetPackageStatus(package, status, actor);
            var component = package.Components.Single();
            component.SessionCount = request.Credits;
            component.DurationDays = null;
            component.ApplicableTrainerLevel = level;
            if (id is not null && (component.PTFreezeMinimumDays != request.FreezeMinimumDays ||
                component.PTFreezeMaxTotalDays != request.FreezeMaxTotalDays))
                Audit(actor, nameof(PackageComponent), component.Id,
                    JsonSerializer.Serialize(new { request.FreezeMinimumDays, request.FreezeMaxTotalDays }));
            component.PTFreezeMinimumDays = request.FreezeMinimumDays;
            component.PTFreezeMaxTotalDays = request.FreezeMaxTotalDays;
            if (id is null)
                store.Add(package);
            await store.Save(ct);
            return Result<PTPackageResponse>.Success(PackageResponse(package), id is null ? 201 : 200);
        }, ct);
}
