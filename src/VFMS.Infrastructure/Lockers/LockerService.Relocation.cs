using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    public Task<Result<LockerRentalView>> SelectReplacement(string actor, int id, int lockerId, CancellationToken ct) =>
        Write(async () =>
        {
            var member = await Actor(actor, UserRole.Member, ct);
            if (member is null)
                return Forbidden<LockerRentalView>();
            var rental = await Rentals.SingleOrDefaultAsync(r => r.Id == id && r.CustomerUserId == actor &&
                r.Locker.LockerZone.GymId == member.GymId, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var now = Now.UtcDateTime;
            if (!CanRelocate(rental, now) || lockerId == rental.LockerId)
                return Conflict<LockerRentalView>("Chỉ đổi tủ đang thuê khi tủ cũ cần bảo trì.");
            if (rental.ReplacementLockerId == lockerId)
                return Result<LockerRentalView>.Success(View(rental, now));
            var replacement = await db.Lockers.SingleOrDefaultAsync(l => l.Id == lockerId &&
                l.LockerZone.GymId == member.GymId, ct);
            if (replacement is null)
                return Missing<LockerRentalView>();
            if (replacement.LockerStatus is LockerStatus.Rented or LockerStatus.Maintenance or LockerStatus.Damaged ||
                await Allocations(db.LockerRentalContracts, now).AnyAsync(r =>
                    r.LockerId == lockerId || r.ReplacementLockerId == lockerId, ct))
                return Conflict<LockerRentalView>("Tủ thay thế không còn trống.");

            ReleaseReplacement(rental);
            rental.ReplacementLocker = replacement;
            rental.ReplacementLockerId = replacement.Id;
            replacement.LockerStatus = LockerStatus.Reserved;
            Audit(actor, rental, $"LockerReplacementSelected:{replacement.Id}", now);
            await db.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public Task<Result<LockerRentalView>> ConfirmRelocation(string actor, int id, int lockerId, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerRentalView>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var now = Now.UtcDateTime;
            if (rental.LockerId == lockerId && rental.ReplacementLockerId is null && InUse(rental))
                return Result<LockerRentalView>.Success(View(rental, now));
            if (!CanRelocate(rental, now) || rental.ReplacementLocker is not { LockerStatus: LockerStatus.Reserved } replacement ||
                replacement.Id != lockerId)
                return Conflict<LockerRentalView>("Không còn yêu cầu đổi sang tủ này.");
            var oldId = rental.LockerId;
            rental.Locker = replacement;
            rental.LockerId = replacement.Id;
            rental.ReplacementLocker = null;
            rental.ReplacementLockerId = null;
            replacement.LockerStatus = LockerStatus.Rented;
            Audit(actor, rental, $"LockerRelocated:{oldId}:{replacement.Id}", now);
            Notify(rental, "Đã đổi tủ thuê", $"Hợp đồng hiện tại đã chuyển sang tủ {replacement.LockerNumber}.", now);
            await db.SaveChangesAsync(ct);
            await db.Entry(replacement).Reference(l => l.LockerZone).LoadAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    private static bool CanRelocate(LockerRentalContract rental, DateTime now) =>
        InUse(rental) && rental.KeyLossConfirmedAt is null && now < RentalExpiry(rental) &&
        rental.Locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged;

    private static void ReleaseReplacement(LockerRentalContract rental)
    {
        if (rental.ReplacementLocker is { LockerStatus: LockerStatus.Reserved } replacement)
            replacement.LockerStatus = LockerStatus.Available;
        rental.ReplacementLocker = null;
        rental.ReplacementLockerId = null;
    }
}
