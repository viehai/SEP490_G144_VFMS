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

    public async Task<T> WithMemberVisitAsync<T>(string userId, Func<ApplicationUser?, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        // Hold the member row until the visit is committed.
        var users = Context.Database.IsSqlServer()
            ? Context.ApplicationUsers.FromSqlInterpolated($"SELECT * FROM [ApplicationUsers] WITH (UPDLOCK) WHERE [UserId] = {userId}")
            : Context.ApplicationUsers.Where(user => user.UserId == userId);
        var result = await action(await users.AsNoTracking().SingleOrDefaultAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<CheckIn?> GetOpenCheckInAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.CheckIns.FirstOrDefaultAsync(c => c.MemberUserId == userId && c.Status == CheckInStatus.Active && c.CheckoutTime == null, cancellationToken);

    public Task<CheckIn?> GetCheckInAsync(int id, CancellationToken cancellationToken = default) =>
        Context.CheckIns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<(ApplicationUser User, bool IsCheckedIn)>> SearchMembersAsync(
        string term, CancellationToken cancellationToken = default)
    {
        var users = await Context.ApplicationUsers.AsNoTracking()
            .Where(u => u.UserRoles.Any(r => r.Role.RoleName == "Member" || r.Role.RoleName == "Customer"))
            .Where(u => u.FullName.Contains(term) || u.PhoneNumber.Contains(term) ||
                        Context.MemberMemberships.Any(m => m.MemberUserId == u.UserId && m.OrderItem.Order.OrderNumber == term) ||
                        Context.PTPurchases.Any(p => p.MemberUserId == u.UserId && p.Order != null && p.Order.OrderNumber == term))
            .OrderBy(u => u.FullName).Take(20)
            .ToListAsync(cancellationToken);
        var ids = users.Select(u => u.UserId).ToList();
        var inside = await Context.CheckIns.Where(c => ids.Contains(c.MemberUserId) && c.Status == CheckInStatus.Active && c.CheckoutTime == null)
            .Select(c => c.MemberUserId).ToListAsync(cancellationToken);
        return users.Select(u => (u, inside.Contains(u.UserId))).ToList();
    }
}
