using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
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

    public async Task<DateTime?> GetLastCheckInTimeAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.CheckIns.Where(c => c.MemberUserId == userId)
            .OrderByDescending(c => c.CheckinTime).Select(c => (DateTime?)c.CheckinTime).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PTSession>> GetScheduledPtSessionsAsync(string userId, DateOnly date, CancellationToken cancellationToken = default) =>
        await Context.PTSessions.Include(s => s.PTPurchase)
            .Where(s => s.MemberUserId == userId && s.SessionDate == date && s.SessionStatus == PTSessionStatus.Scheduled)
            .ToListAsync(cancellationToken);
}
