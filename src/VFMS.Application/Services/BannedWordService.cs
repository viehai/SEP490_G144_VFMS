using System.Text.RegularExpressions;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>UC-85 Manage Banned Words. GBR-65 (audit), GBR-66 (content screening).</summary>
public sealed class BannedWordService : IBannedWordService
{
    private const string AuditEntity = nameof(BannedWordList);

    private readonly IBannedWordRepository _repository;
    private readonly IMapper _mapper;

    public BannedWordService(IBannedWordRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResponse<BannedWordResponse>>> GetWordsAsync(
        BannedWordListRequest request, CancellationToken cancellationToken = default)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : Normalize(request.Search);
        var (words, total) = await _repository.SearchAsync(
            search, request.IsActive, request.Page, request.PageSize, cancellationToken);
        var items = words.Select(_mapper.Map<BannedWordResponse>).ToArray();
        return Result<PagedResponse<BannedWordResponse>>.Success(
            new PagedResponse<BannedWordResponse>(request.Page, request.PageSize, total, items));
    }

    public async Task<Result<BannedWordResponse>> GetWordAsync(int id, CancellationToken cancellationToken = default)
    {
        var word = await _repository.GetByIdAsync(id, cancellationToken);
        return word is null ? NotFound() : Result<BannedWordResponse>.Success(_mapper.Map<BannedWordResponse>(word));
    }

    public async Task<Result<BannedWordResponse>> CreateAsync(
        BannedWordRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request.Word);
        var existing = await _repository.FindByWordAsync(normalized, cancellationToken);
        if (existing is not null)
            return Duplicate(existing);

        var word = new BannedWordList
        {
            Word = normalized,
            IsActive = true,
            CreatedByUserId = actorUserId
        };
        var audit = CreateAudit(actorUserId, AuditActionType.Create, ipAddress, null, $"Word={normalized}; IsActive=True");
        await _repository.AddAsync(word, audit, cancellationToken);

        var saved = await _repository.GetByIdAsync(word.Id, cancellationToken) ?? word;
        return Result<BannedWordResponse>.Success(_mapper.Map<BannedWordResponse>(saved), 201);
    }

    public async Task<Result<BannedWordResponse>> UpdateAsync(
        int id, BannedWordRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var word = await _repository.GetByIdAsync(id, cancellationToken);
        if (word is null)
            return NotFound();

        var normalized = Normalize(request.Word);
        if (string.Equals(word.Word, normalized, StringComparison.Ordinal))
            return Result<BannedWordResponse>.Success(_mapper.Map<BannedWordResponse>(word));

        var existing = await _repository.FindByWordAsync(normalized, cancellationToken);
        if (existing is not null && existing.Id != word.Id)
            return Duplicate(existing);

        var audit = CreateAudit(actorUserId, AuditActionType.Update, ipAddress, $"Word={word.Word}", $"Word={normalized}");
        audit.EntityId = word.Id;
        word.Word = normalized;
        await _repository.UpdateAsync(word, audit, cancellationToken);
        return Result<BannedWordResponse>.Success(_mapper.Map<BannedWordResponse>(word));
    }

    public async Task<Result<BannedWordResponse>> SetActiveAsync(
        int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var word = await _repository.GetByIdAsync(id, cancellationToken);
        if (word is null)
            return NotFound();

        if (word.IsActive == isActive)
            return isActive
                ? Result<BannedWordResponse>.Failure("The banned word is already active.", "BANNED_WORD_ALREADY_ACTIVE", 409)
                : Result<BannedWordResponse>.Failure("The banned word is already inactive.", "BANNED_WORD_ALREADY_INACTIVE", 409);

        var audit = CreateAudit(actorUserId, AuditActionType.Update, ipAddress,
            $"IsActive={word.IsActive}", $"IsActive={isActive}");
        audit.EntityId = word.Id;
        word.IsActive = isActive;
        await _repository.UpdateAsync(word, audit, cancellationToken);
        return Result<BannedWordResponse>.Success(_mapper.Map<BannedWordResponse>(word));
    }

    public async Task<IReadOnlyList<string>> FindBannedWordsAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var words = await _repository.GetActiveWordsAsync(cancellationToken);
        return words
            .Where(word => !string.IsNullOrWhiteSpace(word) && ContainsWholeWord(text, word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Trims, collapses inner whitespace and lower-cases a word so duplicates compare equal.</summary>
    public static string Normalize(string word) =>
        Regex.Replace(word.Trim(), @"\s+", " ").ToLowerInvariant();

    public static bool ContainsWholeWord(string text, string word)
    {
        var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(word).Replace(@"\ ", @"\s+") + @"(?![\p{L}\p{N}])";
        return Regex.IsMatch(text, pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    }

    private static Result<BannedWordResponse> Duplicate(BannedWordList existing) =>
        existing.IsActive
            ? Result<BannedWordResponse>.Failure(
                "This banned word already exists.", "BANNED_WORD_EXISTS", 409)
            : Result<BannedWordResponse>.Failure(
                $"This banned word already exists but is inactive (id {existing.Id}). Activate it instead.",
                "BANNED_WORD_INACTIVE_EXISTS", 409);

    private static Result<BannedWordResponse> NotFound() =>
        Result<BannedWordResponse>.Failure("Banned word was not found.", "BANNED_WORD_NOT_FOUND", 404);

    private static AuditLog CreateAudit(
        string actorUserId, AuditActionType action, string? ipAddress, string? oldValue, string? newValue) =>
        new()
        {
            ActorUserId = actorUserId,
            ActionType = action,
            AuditEntity = AuditEntity,
            OldValue = oldValue,
            NewValue = newValue,
            OccurredAt = DateTime.UtcNow,
            IpAddress = ipAddress
        };
}
