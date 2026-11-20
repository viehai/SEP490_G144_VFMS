using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Contracts.Feedback;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class FeedbackRepository : IFeedbackRepository
{
    private readonly VfmsDbContext _context;

    public FeedbackRepository(VfmsDbContext context) => _context = context;

    public async Task<(IReadOnlyList<TrainerReview> Items, int Total)> SearchAsync(
        string? search, string? status, int? rating, string? memberUserId, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TrainerReviews
            .AsNoTracking()
            .Include(review => review.TrainerUser)
            .Include(review => review.MemberUser)
            .Include(review => review.ReviewedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(review =>
                (review.Comment != null && review.Comment.Contains(search)) ||
                review.MemberUser.FullName.Contains(search) ||
                (review.TrainerUser != null && review.TrainerUser.FullName.Contains(search)));
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TrainerReviewStatus>(status, true, out var parsed))
            query = query.Where(review => review.ReviewStatus == parsed);
        if (rating is >= 1 and <= 5)
            query = query.Where(review => review.Rating == rating.Value);
        if (!string.IsNullOrWhiteSpace(memberUserId))
            query = query.Where(review => review.MemberUserId == memberUserId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<FeedbackTrainerResponse>> GetTrainersAsync(
        CancellationToken cancellationToken = default) =>
        await _context.TrainerProfiles
            .AsNoTracking()
            .Include(profile => profile.PTUser)
            .Where(profile => profile.IsActive)
            .OrderBy(profile => profile.PTUser.FullName)
            .Select(profile => new FeedbackTrainerResponse(
                profile.PTUserId,
                profile.PTUser.FullName,
                profile.AvatarUrl,
                profile.Bio,
                profile.Level.ToString()))
            .ToListAsync(cancellationToken);

    public Task<TrainerReview?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _context.TrainerReviews
            .Include(review => review.TrainerUser)
            .Include(review => review.MemberUser)
            .Include(review => review.ReviewedByUser)
            .FirstOrDefaultAsync(review => review.Id == id, cancellationToken);

    public Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public Task<TrainerReview?> FindExistingAsync(
        string trainerUserId, string memberUserId, CancellationToken cancellationToken = default) =>
        _context.TrainerReviews.FirstOrDefaultAsync(
            review => review.TrainerUserId == trainerUserId && review.MemberUserId == memberUserId,
            cancellationToken);

    public async Task SaveAsync(
        TrainerReview review, AuditLog? audit = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (review.Id == 0)
            _context.TrainerReviews.Add(review);
        if (audit is not null)
        {
            audit.EntityId = review.Id;
            _context.AuditLogs.Add(audit);
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
