using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class BannedWordRepository : IBannedWordRepository
{
    private readonly VfmsDbContext _context;

    public BannedWordRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<BannedWordList> Words, int TotalCount)> SearchAsync(
        string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.BannedWordLists.AsNoTracking().Include(word => word.Admin).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(word => word.Word.Contains(search));
        if (isActive.HasValue)
            query = query.Where(word => word.IsActive == isActive.Value);

        var total = await query.CountAsync(cancellationToken);
        var words = await query
            .OrderBy(word => word.Word)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (words, total);
    }

    public Task<BannedWordList?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.BannedWordLists
            .Include(word => word.Admin)
            .FirstOrDefaultAsync(word => word.Id == id, cancellationToken);

    public Task<BannedWordList?> FindByWordAsync(string normalizedWord, CancellationToken cancellationToken = default) =>
        _context.BannedWordLists
            .FirstOrDefaultAsync(word => word.Word.ToLower() == normalizedWord, cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveWordsAsync(CancellationToken cancellationToken = default) =>
        await _context.BannedWordLists
            .AsNoTracking()
            .Where(word => word.IsActive)
            .Select(word => word.Word)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        _context.BannedWordLists.Add(word);
        await _context.SaveChangesAsync(cancellationToken);

        auditLog.EntityId = word.Id;
        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly VfmsDbContext _context;

    public AuditLogRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> SearchAsync(
        DateTime? fromUtc, DateTime? toUtcExclusive, string? actorUserId, AuditActionType? actionType,
        string? entity, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs.AsNoTracking().Include(log => log.ActorUser).AsQueryable();
        if (fromUtc.HasValue)
            query = query.Where(log => log.OccurredAt >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            query = query.Where(log => log.OccurredAt < toUtcExclusive.Value);
        if (actorUserId is not null)
            query = query.Where(log => log.ActorUserId == actorUserId);
        if (actionType.HasValue)
            query = query.Where(log => log.ActionType == actionType.Value);
        if (entity is not null)
            query = query.Where(log => log.AuditEntity.StartsWith(entity));

        var total = await query.CountAsync(cancellationToken);
        var logs = await query
            .OrderByDescending(log => log.OccurredAt)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (logs, total);
    }

    public Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.AuditLogs
            .AsNoTracking()
            .Include(log => log.ActorUser)
            .FirstOrDefaultAsync(log => log.Id == id, cancellationToken);
}
