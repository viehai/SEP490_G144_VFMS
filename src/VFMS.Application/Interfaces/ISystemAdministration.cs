using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IBannedWordRepository
{
    Task<(IReadOnlyList<BannedWordList> Words, int TotalCount)> SearchAsync(
        string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BannedWordList?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Finds a word (active or inactive) by its normalized text.</summary>
    Task<BannedWordList?> FindByWordAsync(string normalizedWord, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveWordsAsync(CancellationToken cancellationToken = default);
    /// <summary>Saves a new word and its audit entry in one transaction.</summary>
    Task AddAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default);
    /// <summary>Saves changes to a tracked word together with its audit entry.</summary>
    Task UpdateAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default);
}

/// <summary>UC-85 Manage Banned Words, plus the content check used by reviews (GBR-66).</summary>
public interface IBannedWordService
{
    Task<Result<PagedResponse<BannedWordResponse>>> GetWordsAsync(
        BannedWordListRequest request, CancellationToken cancellationToken = default);
    Task<Result<BannedWordResponse>> GetWordAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<BannedWordResponse>> CreateAsync(
        BannedWordRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<BannedWordResponse>> UpdateAsync(
        int id, BannedWordRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<BannedWordResponse>> SetActiveAsync(
        int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active banned words found in <paramref name="text"/> (whole words, case-insensitive).
    /// An empty list means the text is allowed. Intended for review/feedback/bio screening (GBR-66).
    /// </summary>
    Task<IReadOnlyList<string>> FindBannedWordsAsync(string? text, CancellationToken cancellationToken = default);
}

public interface IAuditLogRepository
{
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> SearchAsync(
        DateTime? fromUtc, DateTime? toUtcExclusive, string? actorUserId, AuditActionType? actionType,
        string? entity, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>UC-87 View System Audit Logs (read-only, GBR-65).</summary>
public interface IAuditLogService
{
    Task<Result<PagedResponse<AuditLogResponse>>> GetLogsAsync(
        AuditLogListRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuditLogResponse>> GetLogAsync(int id, CancellationToken cancellationToken = default);
}
