using VFMS.Contracts.Yoga.Validators;
using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Feedback;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class FeedbackRepository
{
    public Task<Package?> GetReviewablePackageAsync(int packageId, CancellationToken ct) =>
        _context.Packages.Include(p => p.Components).SingleOrDefaultAsync(p =>
            p.Id == packageId && p.PackageStatus == PackageStatus.Active && p.Components.Count == 1, ct);

    public async Task<PackageReviewsResponse> ListPackageReviewsAsync(int packageId, int page, int? rating, CancellationToken ct)
    {
        var published = ModeratedFeedback.AsNoTracking().Where(r => r.PackageId == packageId && !r.MemberUser.IsDeleted &&
            r.ReviewStatus == TrainerReviewStatus.Approved && r.IsVisible);
        var summary = await published.GroupBy(r => r.PackageId)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .SingleOrDefaultAsync(ct);
        var total = summary?.Count ?? 0;
        var filtered = rating.HasValue ? published.Where(r => r.Rating == rating) : published;
        var matches = rating.HasValue && total > 0 ? await filtered.CountAsync(ct) : total;
        const int size = PackageReviewsResponse.PageSize;
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling((double)matches / size)));
        var items = await filtered.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new PublicPackageReview(r.Id, r.MemberUser.FullName, r.Rating, r.Comment, r.CreatedAt, r.ManagerReply))
            .ToListAsync(ct);
        return new(total, summary?.Average, new(page, size, matches, items));
    }

    public Task<TrainerReviewStatus?> GetPackageReviewStatusAsync(int packageId, string memberId, CancellationToken ct) =>
        ModeratedFeedback.Where(r => r.PackageId == packageId && r.MemberUserId == memberId)
            .Select(r => (TrainerReviewStatus?)r.ReviewStatus).SingleOrDefaultAsync(ct);

    public Task<bool> HasUsedPackageAsync(int packageId, ComponentType type, string memberId, CancellationToken ct) =>
        type switch
        {
            ComponentType.GYM => _context.CheckIns.AnyAsync(c => c.MemberUserId == memberId &&
                c.MemberMembership != null && c.MemberMembership.MemberUserId == memberId &&
                c.MemberMembership.OrderItem.PackageId == packageId && c.MemberMembership.OrderItem.BeneficiaryUserId == memberId, ct),
            ComponentType.YOGA => HasAttendedYogaAsync(packageId, memberId, ct),
            // A PT purchase normally points at the qualifying Gym membership. The PT package is on its order.
            ComponentType.PT => _context.PTSessions.AnyAsync(s => s.SessionStatus == PTSessionStatus.Completed &&
                s.MemberUserId == memberId && s.PTPurchase.MemberUserId == memberId &&
                (s.PTPurchase.Order != null
                    ? s.PTPurchase.Order.Items.Any(i => i.PackageId == packageId && i.BeneficiaryUserId == memberId) &&
                      !s.PTPurchase.Order.Items.Any(i => i.BeneficiaryUserId == memberId && i.PackageId != packageId &&
                          i.Package.Components.Any(c => c.ComponentType == ComponentType.PT))
                    : s.PTPurchase.MemberMembership.PackageComponent.ComponentType == ComponentType.PT &&
                      s.PTPurchase.MemberMembership.MemberUserId == memberId &&
                      s.PTPurchase.MemberMembership.OrderItem.PackageId == packageId &&
                      s.PTPurchase.MemberMembership.OrderItem.BeneficiaryUserId == memberId), ct),
            _ => Task.FromResult(false)
        };

    private async Task<bool> HasAttendedYogaAsync(int packageId, string memberId, CancellationToken ct)
    {
        var attended = _context.YogaBookings.Where(b => b.MemberUserId == memberId &&
            b.Attendances.Any(a => a.MemberUserId == memberId && a.AttendanceStatus == YogaAttendanceStatus.Present));
        if (await attended.AnyAsync(b => b.OrderItem != null && b.OrderItem.BeneficiaryUserId == memberId &&
                b.OrderItem.PackageId == packageId, ct)) return true;

        // Old bookings did not record the paying order line. Attribute them only if a single package
        // could have covered this member and the class level on the gym-local date of enrolment.
        var legacyBookings = await attended.Where(b => b.OrderItemId == null)
            .Select(b => new { b.BookedAt, b.YogaClass.DifficultyLevel }).ToListAsync(ct);
        if (legacyBookings.Count == 0) return false;
        var entitlements = await _context.MemberMemberships.Where(m => m.MemberUserId == memberId &&
                m.OrderItem.BeneficiaryUserId == memberId && m.PackageComponent.ComponentType == ComponentType.YOGA)
            .Select(m => new { m.OrderItem.PackageId, m.PackageComponent.YogaDifficultyLevel, m.CreatedAt, m.StartDate, m.EndDate })
            .ToListAsync(ct);
        foreach (var booking in legacyBookings)
        {
            var date = DateOnly.FromDateTime(booking.BookedAt.AddHours(7));
            var candidates = entitlements.Where(m => m.CreatedAt <= booking.BookedAt && m.StartDate <= date && m.EndDate >= date &&
                YogaRules.PlanCovers(m.YogaDifficultyLevel, booking.DifficultyLevel))
                .Select(m => m.PackageId).Distinct().Take(2).ToArray();
            if (candidates.Length == 1 && candidates[0] == packageId) return true;
        }
        return false;
    }

    public async Task<bool> TryAddPackageReviewAsync(TrainerReview review, CancellationToken ct)
    {
        try
        {
            await SaveAsync(review, cancellationToken: ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _context.Entry(review).State = EntityState.Detached;
            // Another submission can win between the eligibility check and the unique index insert.
            if (await GetPackageReviewStatusAsync(review.PackageId!.Value, review.MemberUserId, ct) != null) return false;
            throw;
        }
    }
}
