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

        var slots = (await Templates(trainerId, ct)).OrderBy(s => s.StartTime).ThenBy(s => s.DayOfWeek)
            .Select(s => new PTWeeklySlot(s.Id, s.DayOfWeek!.Value, s.StartTime, s.EndTime, null)).ToArray();
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
        var templates = await Templates(trainerId, ct);
        var booked = await repo.GetOccupyingSessionsAsync(Now, from, through, trainerId, null, cancellationToken: ct);
        var yoga = await repo.GetYogaSessionsAsync(trainerId, null, from, through, ct);
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
        IReadOnlyList<PTSlotInput> slots = (await Templates(actor, ct)).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new PTSlotInput(x.DayOfWeek!.Value, x.StartTime)).ToList();
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
        if (await TeachesYoga(actor, ct))
            return Conflict<bool>("Huấn luyện viên đang phụ trách lớp Yoga.");
        if (request.Slots.Any(s => s is null))
            return Invalid<bool>("Mẫu lịch chứa ca không hợp lệ.");
        var desired = request.Slots.Distinct().ToArray();
        if (desired.Length != request.Slots.Length ||
            desired.Any(s => !Enum.IsDefined(s.DayOfWeek) || !InHours(trainer.Gym, s.StartTime, s.StartTime.AddHours(1))))
            return Invalid<bool>("Ca phải hợp lệ, không trùng nhau và nằm trong giờ hoạt động của gym.");
        var existing = await Templates(actor, ct);
        var added = desired.Where(slot => !existing.Any(x => x.DayOfWeek == slot.DayOfWeek && x.StartTime == slot.StartTime)).ToArray();
        if (added.Any(slot => !IsWholeHour(slot.StartTime)))
            return Invalid<bool>("Ca mới phải bắt đầu vào giờ tròn, ví dụ 06:00 hoặc 07:00.");
        // UC 4.1 / GBR-08: removing a slot affects new purchases only; held and confirmed sessions keep their time.
        // A new slot that overlaps a session booked on one date is still published; that date shows as busy.
        var removed = existing.Where(x => !desired.Contains(new(x.DayOfWeek!.Value, x.StartTime))).ToArray();

        foreach (var slot in removed)
            slot.IsDeleted = true;
        foreach (var s in added)
            repo.Add(new PTAvailability
            {
                PTUserId = actor,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.StartTime.AddHours(1)
            });
        await repo.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }, ct);

    public async Task<Result<IReadOnlyList<PTPackageResponse>>> Packages(bool all, CancellationToken ct)
    {
        var packages = await repo.GetPTPackagesAsync(activeOnly: !all, ct);
        if (!all)
            return Result<IReadOnlyList<PTPackageResponse>>.Success(packages.Select(p => PackageResponse(p)).ToArray());

        var registrations = await repo.CountRunningContractsByPackageAsync(Today, ct);
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
            var package = await repo.GetPTPackageAsync(id, activeOnly: false, ct);
            if (package is null)
                return Missing<bool>();

            SetPackageStatus(package, isActive ? PackageStatus.Active : PackageStatus.Inactive, actor);
            await repo.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<PTPackageDeleteResponse>> DeletePackage(string actor, int id, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Manager, ct) is null)
                return Forbidden<PTPackageDeleteResponse>();
            var package = await repo.GetPTPackageAsync(id, activeOnly: false, ct);
            if (package is null)
                return Missing<PTPackageDeleteResponse>();

            // Hidden references still belong to the order and subscription history.
            var hasUsage = await repo.PackageHasHistoryAsync(id, ct);
            if (hasUsage)
                SetPackageStatus(package, PackageStatus.Inactive, actor);
            else
            {
                foreach (var component in package.Components)
                    repo.Remove(component);
                repo.Remove(package);
                repo.Add(new AuditLog
                {
                    ActorUserId = actor,
                    ActionType = AuditActionType.Delete,
                    AuditEntity = nameof(Package),
                    EntityId = id,
                    OldValue = JsonSerializer.Serialize(PackageResponse(package)),
                    OccurredAt = Now
                });
            }
            await repo.SaveChangesAsync(ct);
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
            if (string.IsNullOrWhiteSpace(request.Name) || request.Credits is <= 0 or > PTPackageRequestValidator.MaxSessions ||
                request.Price <= 0 ||
                request.Price > 9_999_999_999_999_999m || decimal.Truncate(request.Price) != request.Price ||
                !Enum.TryParse<TrainerLevel>(request.TrainerLevel, true, out var level) ||
                !string.Equals(request.TrainerLevel, level.ToString(), StringComparison.OrdinalIgnoreCase))
                return Invalid<PTPackageResponse>("Tên, số buổi, giá hoặc cấp PT không hợp lệ.");
            if (request.FreezeMinimumDays < 1 || request.FreezeMaxTotalDays < request.FreezeMinimumDays)
                return Invalid<PTPackageResponse>("Số ngày bảo lưu tối thiểu phải dương và không vượt tổng hạn mức.");
            var package = id is { } packageId ? await repo.GetPTPackageAsync(packageId, activeOnly: false, ct) :
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
                repo.Add(package);
            await repo.SaveChangesAsync(ct);
            return Result<PTPackageResponse>.Success(PackageResponse(package), id is null ? 201 : 200);
        }, ct);
}
