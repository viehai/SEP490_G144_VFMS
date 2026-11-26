using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

// UC 5.7 Manage Locker Catalog & Pricing (Manager). The price list itself is saved by SavePricing.
public sealed partial class LockerService
{
    private void AuditCatalog(string actor, string entity, int? id, string value, DateTime now) =>
        repo.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = entity,
            EntityId = id,
            NewValue = value,
            OccurredAt = now
        });

    public async Task<Result<LockerCatalog>> Catalog(string actor, CancellationToken ct)
    {
        var manager = await Actor(actor, UserRole.Manager, ct);
        if (manager is null)
            return Forbidden<LockerCatalog>();
        var now = Now.UtcDateTime;
        var zones = await repo.GetZonesAsync(manager.GymId, ct);
        var byLocker = (await repo.GetGymAllocationsAsync(manager.GymId, now, ct)).ToLookup(r => r.LockerId);
        var replacements = byLocker.SelectMany(g => g).Where(r => r.ReplacementLockerId != null)
            .Select(r => r.ReplacementLockerId!.Value).ToHashSet();
        var prices = (await repo.GetActivePoliciesAsync(ct)).Select(Policy).OrderBy(p => p.Months).ToArray();
        var rules = (await repo.GetGymAsync(manager.GymId, ct))?.LockerRules;
        if (string.IsNullOrWhiteSpace(rules)) rules = LockerRuleDefaults.Text;
        return Result<LockerCatalog>.Success(new(zones.Select(z => new LockerCatalogZone(z.Id, z.ZoneName, z.FloorNumber,
            z.Lockers.Where(l => l.LockerStatus != LockerStatus.Inactive).OrderBy(l => l.LockerNumber).Select(l => new LockerSlot(l.Id, l.LockerNumber, z.ZoneName, z.FloorNumber,
                replacements.Contains(l.Id) ? "Reserved" : SlotStatus(l, byLocker[l.Id].FirstOrDefault(), now))).ToArray(), z.Lockers.Count == 0)).ToArray(),
            prices, rules));
    }

    public Task<Result<LockerCatalogZone>> AddZone(string actor, LockerZoneRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null)
                return Forbidden<LockerCatalogZone>();
            var used = (await repo.GetZonesAsync(manager.GymId, ct))
                .Select(z => z.ZoneName.Trim().Replace("Khu ", "", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var index = 1;
            string name;
            do { name = ZoneLetters(index++); } while (used.Contains(name));
            var zone = new LockerZone { GymId = manager.GymId, ZoneName = name, FloorNumber = 0 };
            repo.Add(zone);
            await repo.SaveChangesAsync(ct);
            AuditCatalog(actor, nameof(LockerZone), zone.Id, $"LockerZoneAdded:{name}", Now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<LockerCatalogZone>.Success(new(zone.Id, zone.ZoneName, zone.FloorNumber, [], true), 201);
        }, ct);

    public Task<Result<int>> DeleteZone(string actor, int zoneId, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null) return Forbidden<int>();
            var zone = await repo.GetZoneAsync(zoneId, manager.GymId, ct);
            if (zone is null) return Missing<int>();
            if (zone.Lockers.Count != 0)
                return Result<int>.Failure("Chỉ xóa được khu chưa có tủ. Khu còn tủ đang dùng hoặc đã ngừng dùng phải được giữ lại.", "LOCKER_ZONE_NOT_EMPTY", 409);
            zone.IsDeleted = true;
            AuditCatalog(actor, nameof(LockerZone), zone.Id, $"LockerZoneDeleted:{zone.ZoneName}", Now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<int>.Success(zone.Id);
        }, ct);

    private static string ZoneLetters(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    public Task<Result<LockerSlot>> AddLocker(string actor, LockerCreateRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null)
                return Forbidden<LockerSlot>();
            var number = input.Number?.Trim();
            if (string.IsNullOrEmpty(number) || number.Length > 20)
                return Invalid<LockerSlot>("Số tủ là bắt buộc và tối đa 20 ký tự.");
            var zone = await repo.GetZoneAsync(input.ZoneId, manager.GymId, ct);
            if (zone is null)
                return Missing<LockerSlot>();
            // MSG-48: the number is unique within the gym.
            if (await repo.LockerNumberExistsAsync(manager.GymId, number, ct))
                return Result<LockerSlot>.Failure($"Số tủ {number} đã tồn tại.", "LOCKER_NUMBER_DUPLICATE", 409);
            var locker = new Locker { LockerZone = zone, LockerNumber = number, LockerStatus = LockerStatus.Available };
            zone.TotalLockers++;
            repo.Add(locker);
            await repo.SaveChangesAsync(ct);
            AuditCatalog(actor, nameof(Locker), locker.Id, $"LockerAdded:{number}", Now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<LockerSlot>.Success(Slot(locker, "Available"), 201);
        }, ct);

    public Task<Result<LockerSlot>> DeactivateLocker(string actor, int lockerId, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null)
                return Forbidden<LockerSlot>();
            var locker = await repo.GetGymLockerAsync(lockerId, manager.GymId, ct);
            if (locker is null)
                return Missing<LockerSlot>();
            var now = Now.UtcDateTime;
            if (locker.LockerStatus == LockerStatus.Inactive)
                return Result<LockerSlot>.Success(Slot(locker, "Inactive"));
            // UC 5.7 Step 3.1: only an Available or Maintenance locker without a hold or contract can be deactivated.
            if (locker.LockerStatus is not (LockerStatus.Available or LockerStatus.Maintenance) ||
                await repo.HasAllocationAsync(now, lockerId, null, null, ct))
                return Held<LockerSlot>(locker.LockerNumber);
            locker.LockerStatus = LockerStatus.Inactive;
            locker.LockerZone.TotalLockers = Math.Max(0, locker.LockerZone.TotalLockers - 1);
            AuditCatalog(actor, nameof(Locker), lockerId, "LockerDeactivated", now);
            await repo.SaveChangesAsync(ct);
            return Result<LockerSlot>.Success(Slot(locker, "Inactive"));
        }, ct);

    public Task<Result<LockerRulesView>> SaveRules(string actor, LockerRulesRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null)
                return Forbidden<LockerRulesView>();
            var text = input.Text?.Trim() ?? string.Empty;
            if (text.Length > 4000)
                return Invalid<LockerRulesView>("Nội quy tối đa 4.000 ký tự.");
            var gym = await repo.GetGymAsync(manager.GymId, ct);
            if (gym is null)
                return Missing<LockerRulesView>();
            gym.LockerRules = text;
            AuditCatalog(actor, nameof(Gym), gym.Id, "LockerRulesSaved", Now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            var deposit = (await repo.GetActivePoliciesAsync(ct)).FirstOrDefault()?.DepositAmount ?? 100_000;
            return Result<LockerRulesView>.Success(new(text, deposit));
        }, ct);

    // UC 5.1 Step 5: any signed-in Member or desk user reads the rules the Manager published.
    public async Task<Result<LockerRulesView>> Rules(string actor, UserRole role, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerRulesView>();
        var text = (await repo.GetGymAsync(user.GymId, ct))?.LockerRules;
        var deposit = (await repo.GetActivePoliciesAsync(ct)).FirstOrDefault()?.DepositAmount ?? 100_000;
        return Result<LockerRulesView>.Success(new(string.IsNullOrWhiteSpace(text) ? LockerRuleDefaults.Text : text, deposit));
    }
}
