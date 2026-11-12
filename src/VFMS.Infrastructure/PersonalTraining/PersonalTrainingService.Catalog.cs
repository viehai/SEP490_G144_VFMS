using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed partial class PersonalTrainingService
{
    public async Task<Result<IReadOnlyList<PTCalendarSlot>>> Calendar(string trainerId, DateOnly from, CancellationToken ct)
    {
        if (from.DayNumber > DateOnly.MaxValue.DayNumber - 13)
            return Invalid<IReadOnlyList<PTCalendarSlot>>("Ngày xem lịch không hợp lệ.");
        var trainer = await User(trainerId, UserRole.PT, ct);
        if (trainer is null)
            return Missing<IReadOnlyList<PTCalendarSlot>>();
        var through = from.AddDays(13);
        var templates = await Templates(trainerId).AsNoTracking().ToListAsync(ct);
        var booked = await Occupied(Now).Where(s => s.PTUserId == trainerId &&
            s.SessionDate >= from && s.SessionDate <= through).ToListAsync(ct);
        var yoga = await Yoga(trainerId, null, from, through).ToListAsync(ct);
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
            .Select(x => new PTSlotInput(x.DayOfWeek!.Value, x.StartTime)).ToListAsync(ct);
        return Result<IReadOnlyList<PTSlotInput>>.Success(slots);
    }

    public Task<Result<bool>> Publish(string actor, PTPublishRequest request, CancellationToken ct) => Write(async () =>
    {
        var trainer = await User(actor, UserRole.PT, ct);
        if (trainer is null)
            return Forbidden<bool>();
        if (request.Slots.Any(s => s is null))
            return Invalid<bool>("Mẫu lịch chứa ca không hợp lệ.");
        var desired = request.Slots.Distinct().ToArray();
        if (desired.Length != request.Slots.Length || desired.Length < 15 ||
            desired.Any(s => !Enum.IsDefined(s.DayOfWeek) || !InHours(trainer.Gym, s.StartTime, s.StartTime.AddHours(1))) ||
            desired.GroupBy(s => s.DayOfWeek).Any(group => group.OrderBy(s => s.StartTime).Zip(
                group.OrderBy(s => s.StartTime).Skip(1), (left, right) =>
                    right.StartTime - left.StartTime < TimeSpan.FromMinutes(75)).Any(x => x)))
            return Invalid<bool>("Cần ít nhất 15 ca 60 phút trong giờ hoạt động, cách nhau ít nhất 15 phút.");

        var existing = await Templates(actor).ToListAsync(ct);
        var removed = existing.Where(x => !desired.Contains(new(x.DayOfWeek!.Value, x.StartTime))).ToArray();
        var removedIds = removed.Select(x => x.Id).ToArray();
        var future = await Occupied(Now).Where(s => s.PTUserId == actor && s.SessionDate >= Today).ToListAsync(ct);
        if (future.Any(s => s.PTAvailabilityId is { } id && removedIds.Contains(id) ||
            removed.Any(slot => slot.DayOfWeek == s.SessionDate.DayOfWeek &&
                slot.StartTime < s.EndTime && s.StartTime < slot.EndTime)))
            return Conflict<bool>("Không thể sửa hoặc tắt ca đang được giữ hoặc đã xác nhận.");
        var added = desired.Where(slot => !existing.Any(x => x.DayOfWeek == slot.DayOfWeek && x.StartTime == slot.StartTime)).ToArray();
        var yoga = await Yoga(actor, null, Today, DateOnly.MaxValue).ToListAsync(ct);
        if (added.Any(slot => future.Any(s => s.SessionDate.DayOfWeek == slot.DayOfWeek &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.StartTime.AddHours(1), s.StartTime, s.EndTime)) ||
            yoga.Any(s => s.ScheduledDate.DayOfWeek == slot.DayOfWeek &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.StartTime.AddHours(1), s.StartTime, s.EndTime))))
            return Conflict<bool>("Ca lặp trùng lịch đã đặt hoặc lớp Yoga đã phân công.");

        foreach (var slot in removed)
            slot.IsDeleted = true;
        db.PTAvailabilities.AddRange(added.Select(s => new PTAvailability
        {
            PTUserId = actor, DayOfWeek = s.DayOfWeek, StartTime = s.StartTime, EndTime = s.StartTime.AddHours(1)
        }));
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }, ct);

    private IQueryable<Package> PTPackages => db.Packages.Include(p => p.Components)
        .Where(p => p.Components.Count == 1 &&
            p.Components.Any(c => c.ComponentType == ComponentType.PT && c.ApplicableTrainerLevel != null));

    public async Task<Result<IReadOnlyList<PTPackageResponse>>> Packages(bool all, CancellationToken ct)
    {
        var packages = await PTPackages.AsNoTracking().Where(p => all || p.PackageStatus == PackageStatus.Active)
            .OrderBy(p => p.Id).ToListAsync(ct);
        return Result<IReadOnlyList<PTPackageResponse>>.Success(packages.Select(PackageResponse).ToArray());
    }

    public async Task<Result<IReadOnlyList<PTPackageResponse>>> ManagerPackages(string actor, CancellationToken ct) =>
        await User(actor, UserRole.Manager, ct) is null ? Forbidden<IReadOnlyList<PTPackageResponse>>() : await Packages(true, ct);

    private static PTPackageResponse PackageResponse(Package p)
    {
        var component = p.Components.Single();
        return new(p.Id, $"PT-{p.Id:D4}", p.Name, p.Description, component.SessionCount!.Value,
            component.DurationDays!.Value, p.Price, component.ApplicableTrainerLevel!.Value.ToString(),
            component.PTFreezeMinimumDays ?? 0, component.PTFreezeMaxTotalDays ?? 0,
            p.PackageStatus == PackageStatus.Active);
    }

    public Task<Result<PTPackageResponse>> SavePackage(string actor, int? id, PTPackageRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Manager, ct) is null)
                return Forbidden<PTPackageResponse>();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Credits <= 0 ||
                request.ValidityDays <= 0 || request.ValidityDays - 1 > DateOnly.MaxValue.DayNumber - Today.DayNumber ||
                request.Price <= 0 ||
                request.Price > 9_999_999_999_999_999m || decimal.Truncate(request.Price) != request.Price ||
                !Enum.TryParse<TrainerLevel>(request.TrainerLevel, true, out var level) ||
                !string.Equals(request.TrainerLevel, level.ToString(), StringComparison.OrdinalIgnoreCase))
                return Invalid<PTPackageResponse>("Tên, số credit, thời hạn, giá hoặc cấp PT không hợp lệ.");
            if (request.FreezeMinimumDays < 1 || request.FreezeMaxTotalDays < request.FreezeMinimumDays)
                return Invalid<PTPackageResponse>("Số ngày bảo lưu tối thiểu phải dương và không vượt tổng hạn mức.");
            var package = id is { } packageId ? await PTPackages.SingleOrDefaultAsync(p => p.Id == packageId, ct) :
                new Package { Components = [new PackageComponent { ComponentType = ComponentType.PT }] };
            if (package is null)
                return Missing<PTPackageResponse>();
            package.Name = request.Name.Trim();
            package.Description = request.Description?.Trim();
            package.Price = request.Price;
            package.PackageStatus = request.IsActive ? PackageStatus.Active : PackageStatus.Inactive;
            var component = package.Components.Single();
            component.SessionCount = request.Credits;
            component.DurationDays = request.ValidityDays;
            component.ApplicableTrainerLevel = level;
            if (id is not null && (component.PTFreezeMinimumDays != request.FreezeMinimumDays ||
                component.PTFreezeMaxTotalDays != request.FreezeMaxTotalDays))
                Audit(actor, nameof(PackageComponent), component.Id,
                    JsonSerializer.Serialize(new { request.FreezeMinimumDays, request.FreezeMaxTotalDays }));
            component.PTFreezeMinimumDays = request.FreezeMinimumDays;
            component.PTFreezeMaxTotalDays = request.FreezeMaxTotalDays;
            if (id is null)
                db.Packages.Add(package);
            await db.SaveChangesAsync(ct);
            return Result<PTPackageResponse>.Success(PackageResponse(package), id is null ? 201 : 200);
        }, ct);
}
