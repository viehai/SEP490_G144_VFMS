using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class CheckInRepository : AuditedRepositoryBase, ICheckInRepository
{
    public CheckInRepository(VfmsDbContext context) : base(context) { }

    // IgnoreQueryFilters: an erased (soft-deleted) record is reused when the user registers again (1:1 with user).
    public Task<FaceData?> GetFaceDataAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.FaceData.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<CheckIn>> GetActiveCheckInsAsync(CancellationToken cancellationToken = default) =>
        await Context.CheckIns.AsNoTracking()
            .Include(c => c.Member)
            .Include(c => c.HandledByUser)
            .Include(c => c.MemberMembership!).ThenInclude(m => m.OrderItem).ThenInclude(i => i.Order)
            .Where(c => c.Status == CheckInStatus.Active && c.CheckoutTime == null)
            .ToListAsync(cancellationToken);

    public Task<int> CountCheckInsSinceAsync(DateTime utcFrom, CancellationToken cancellationToken = default) =>
        Context.CheckIns.CountAsync(c => c.CheckinTime >= utcFrom, cancellationToken);

    public async Task<IReadOnlyList<FaceData>> GetActiveFaceDataAsync(CancellationToken cancellationToken = default) =>
        await Context.FaceData.Where(f => f.IsActive && f.FaceNetEmbedding.Length > 0).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QRCode>> GetActiveQrCodesAsync(string? userId, string? token, CancellationToken cancellationToken = default) =>
        await Context.QRCodes
            .Where(q => q.IsActive && (userId == null || q.UserId == userId) && (token == null || q.QRToken == token))
            .ToListAsync(cancellationToken);

    public Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.MemberMemberships.AsNoTracking().Include(m => m.PackageComponent)
            .Where(m => m.MemberUserId == userId).ToListAsync(cancellationToken);

    public Task<CheckIn?> GetOpenCheckInAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.CheckIns.FirstOrDefaultAsync(c => c.MemberUserId == userId && c.Status == CheckInStatus.Active, cancellationToken);

    public Task<CheckIn?> GetCheckInAsync(int id, CancellationToken cancellationToken = default) =>
        Context.CheckIns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<DateTime?> GetLastCheckInTimeAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.CheckIns.Where(c => c.MemberUserId == userId)
            .OrderByDescending(c => c.CheckinTime).Select(c => (DateTime?)c.CheckinTime).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PTSession>> GetScheduledPtSessionsAsync(string userId, DateOnly date, DateTime now,
        CancellationToken cancellationToken = default) =>
        await Context.PTSessions.Include(s => s.PTPurchase).ThenInclude(p => p.Sessions)
            .Where(s => s.MemberUserId == userId && s.SessionDate == date)
            .Where(PersonalTrainingRules.CheckInCanComplete(now))
            .ToListAsync(cancellationToken);

    public async Task<bool> CompletePtSessionByCheckInAsync(PTSession session, DateTime now, CancellationToken cancellationToken = default)
    {
        var done = await Context.PTSessions.Where(s => s.Id == session.Id).Where(PersonalTrainingRules.CheckInCanComplete(now))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.SessionStatus, PTSessionStatus.Completed)
                .SetProperty(s => s.CheckInCompletedAt, now)
                .SetProperty(s => s.ProposedDate, (DateOnly?)null)
                .SetProperty(s => s.ProposedStartTime, (TimeOnly?)null)
                .SetProperty(s => s.ProposedEndTime, (TimeOnly?)null)
                .SetProperty(s => s.ProposedAdjustmentType, (PTAdjustmentType?)null)
                .SetProperty(s => s.ProposalExpiresAt, (DateTime?)null), cancellationToken) == 1;
        await Context.Entry(session).ReloadAsync(cancellationToken);
        return done;
    }

    public async Task<IReadOnlyList<(ApplicationUser User, IReadOnlyList<MemberMembership> Memberships, bool IsCheckedIn)>> SearchMembersAsync(
        string term, CancellationToken cancellationToken = default)
    {
        var users = await Context.ApplicationUsers.AsNoTracking()
            .Where(u => u.UserRoles.Any(r => r.Role.RoleName == "Member" || r.Role.RoleName == "Customer"))
            .Where(u => u.FullName.Contains(term) || u.PhoneNumber.Contains(term) ||
                        Context.MemberMemberships.Any(m => m.MemberUserId == u.UserId && m.OrderItem.Order.OrderNumber == term))
            .OrderBy(u => u.FullName).Take(20)
            .ToListAsync(cancellationToken);
        var ids = users.Select(u => u.UserId).ToList();
        var memberships = await Context.MemberMemberships.AsNoTracking().Include(m => m.PackageComponent)
            .Where(m => ids.Contains(m.MemberUserId)).ToListAsync(cancellationToken);
        var inside = await Context.CheckIns.Where(c => ids.Contains(c.MemberUserId) && c.Status == CheckInStatus.Active)
            .Select(c => c.MemberUserId).ToListAsync(cancellationToken);
        return users.Select(u => (u, (IReadOnlyList<MemberMembership>)memberships.Where(m => m.MemberUserId == u.UserId).ToList(),
            inside.Contains(u.UserId))).ToList();
    }
}
