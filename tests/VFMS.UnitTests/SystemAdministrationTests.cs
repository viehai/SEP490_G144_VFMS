using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.SystemAdministration.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class SystemAdministrationTests
{
    // ── Validators ───────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BannedWordValidatorRejectsBlankWord(string word) =>
        Assert.False(new BannedWordRequestValidator().Validate(new BannedWordRequest(word)).IsValid);

    [Fact]
    public void BannedWordValidatorRejectsWordsLongerThan100Characters() =>
        Assert.False(new BannedWordRequestValidator().Validate(new BannedWordRequest(new string('a', 101))).IsValid);

    [Fact]
    public void AuditLogValidatorRejectsStartAfterEndAndUnknownAction()
    {
        var validator = new AuditLogListRequestValidator();
        Assert.False(validator.Validate(new AuditLogListRequest(
            From: new DateTime(2026, 11, 5), To: new DateTime(2026, 11, 4))).IsValid);
        Assert.False(validator.Validate(new AuditLogListRequest(ActionType: "Hack")).IsValid);
        Assert.True(validator.Validate(new AuditLogListRequest(ActionType: "rolechange")).IsValid);
    }

    // ── Banned word service ──────────────────────────────────────

    [Fact]
    public async Task CreateNormalizesWordAndWritesAuditEntry()
    {
        var repository = new BannedWordRepositoryFake();
        var result = await CreateBannedWordService(repository).CreateAsync(
            new BannedWordRequest("  Bad   WORD "), "admin-1", "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("bad word", result.Data!.Word);
        Assert.True(result.Data.IsActive);
        var audit = Assert.Single(repository.Audits);
        Assert.Equal(AuditActionType.Create, audit.ActionType);
        Assert.Equal("BannedWordList", audit.AuditEntity);
        Assert.Equal("admin-1", audit.ActorUserId);
    }

    [Fact]
    public async Task CreateRejectsDuplicateIgnoringCase()
    {
        var repository = new BannedWordRepositoryFake();
        repository.Seed("spam", isActive: true);

        var result = await CreateBannedWordService(repository).CreateAsync(new BannedWordRequest("SPAM"), "admin-1", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("BANNED_WORD_EXISTS", result.ErrorCode);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task CreateMatchingInactiveWordAsksToReactivate()
    {
        var repository = new BannedWordRepositoryFake();
        repository.Seed("spam", isActive: false);

        var result = await CreateBannedWordService(repository).CreateAsync(new BannedWordRequest("spam"), "admin-1", null);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("BANNED_WORD_INACTIVE_EXISTS", result.ErrorCode);
    }

    [Fact]
    public async Task DeactivateThenDeactivateAgainIsRejected()
    {
        var repository = new BannedWordRepositoryFake();
        var word = repository.Seed("spam", isActive: true);
        var service = CreateBannedWordService(repository);

        var first = await service.SetActiveAsync(word.Id, false, "admin-1", null);
        var second = await service.SetActiveAsync(word.Id, false, "admin-1", null);

        Assert.True(first.IsSuccess);
        Assert.False(first.Data!.IsActive);
        Assert.Equal("BANNED_WORD_ALREADY_INACTIVE", second.ErrorCode);
        Assert.Single(repository.Audits);
    }

    [Fact]
    public async Task UpdateToAnotherExistingWordIsRejected()
    {
        var repository = new BannedWordRepositoryFake();
        var word = repository.Seed("spam", isActive: true);
        repository.Seed("scam", isActive: true);

        var result = await CreateBannedWordService(repository).UpdateAsync(
            word.Id, new BannedWordRequest("Scam"), "admin-1", null);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("spam", word.Word);
    }

    [Fact]
    public async Task FindBannedWordsMatchesWholeActiveWordsOnly()
    {
        var repository = new BannedWordRepositoryFake();
        repository.Seed("ass", isActive: true);
        repository.Seed("bad word", isActive: true);
        repository.Seed("idiot", isActive: false);
        var service = CreateBannedWordService(repository);

        Assert.Empty(await service.FindBannedWordsAsync("A classic class, great service."));
        Assert.Empty(await service.FindBannedWordsAsync("The trainer was an idiot."));
        Assert.Equal(["ass"], await service.FindBannedWordsAsync("What an ASS!"));
        Assert.Equal(["bad word"], await service.FindBannedWordsAsync("That is a Bad   word here"));
        Assert.Empty(await service.FindBannedWordsAsync(null));
    }

    [Theory]
    [InlineData("đồ ngu", "Ông là đồ ngu!", true)]
    [InlineData("đồ ngu", "Ông là do ngu!", true)]          // typed without accents
    [InlineData("đồ ngu", "Ông là ĐỒ NGU", true)]
    [InlineData("lừa đảo", "phong tap lua dao khach", true)]
    [InlineData("vô dụng", "Máy này vợ dùng rất thích", false)] // a different phrase with accents is never caught
    [InlineData("vô dụng", "may nay vo dung", true)]
    [InlineData("đồ ngu", "đồ ngủ đẹp", false)]                // "ngủ" (sleep) is written with its accent
    [InlineData("ngu", "Nguyễn Văn A", false)]                 // whole words only
    public void BannedWordsMatchWithOrWithoutAccents(string word, string text, bool blocked) =>
        Assert.Equal(blocked, BannedWordService.ContainsWholeWord(text, word));

    // ── Audit log service ────────────────────────────────────────

    [Fact]
    public async Task AuditLogDateOnlyEndIncludesWholeDay()
    {
        var repository = new AuditLogRepositoryFake();
        var service = new AuditLogService(repository, CreateMapper());

        var result = await service.GetLogsAsync(new AuditLogListRequest(
            From: new DateTime(2026, 11, 1), To: new DateTime(2026, 11, 4), ActionType: "accountlock"));

        // Gym days (UTC+7): 01/11 00:00 to the end of 04/11 in Vietnam is 31/10 17:00 to 04/11 17:00 UTC.
        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0), repository.LastFrom);
        Assert.Equal(new DateTime(2026, 11, 4, 17, 0, 0), repository.LastToExclusive);
        Assert.Equal(AuditActionType.AccountLock, repository.LastActionType);
    }

    [Fact]
    public async Task AuditLogSearchesTheActorByNameOrEmail()
    {
        var repository = new AuditLogRepositoryFake();

        await new AuditLogService(repository, CreateMapper()).GetLogsAsync(new AuditLogListRequest(Actor: "  manager "));

        Assert.Equal("manager", repository.LastActorSearch);
    }

    [Fact]
    public async Task AuditLogRejectsInvalidActionType()
    {
        var result = await new AuditLogService(new AuditLogRepositoryFake(), CreateMapper())
            .GetLogsAsync(new AuditLogListRequest(ActionType: "Hack"));

        Assert.False(result.IsSuccess);
        Assert.Equal("AUDIT_INVALID_ACTION_TYPE", result.ErrorCode);
    }

    [Fact]
    public void AuditLogMapsActionTypeAndActorName()
    {
        var response = CreateMapper().Map<AuditLogResponse>(new AuditLog
        {
            Id = 7,
            ActionType = AuditActionType.RoleChange,
            AuditEntity = "ApplicationUser:u1",
            ActorUserId = "a1",
            ActorUser = new ApplicationUser { UserId = "a1", FullName = "System Admin" }
        });

        Assert.Equal("RoleChange", response.ActionType);
        Assert.Equal("System Admin", response.ActorName);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IMapper CreateMapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(SystemAdministrationMapping).Assembly);
        return new ServiceMapper(new EmptyServiceProvider(), config);
    }

    private static BannedWordService CreateBannedWordService(BannedWordRepositoryFake repository) =>
        new(repository, CreateMapper());

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class BannedWordRepositoryFake : IBannedWordRepository
    {
        private readonly List<BannedWordList> _words = [];
        public List<AuditLog> Audits { get; } = [];

        public BannedWordList Seed(string word, bool isActive)
        {
            var entity = new BannedWordList { Id = _words.Count + 1, Word = word, IsActive = isActive, CreatedByUserId = "admin-1" };
            _words.Add(entity);
            return entity;
        }

        public Task<(IReadOnlyList<BannedWordList> Words, int TotalCount)> SearchAsync(
            string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<BannedWordList>, int)>((_words, _words.Count));

        public Task<BannedWordList?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_words.FirstOrDefault(word => word.Id == id));

        public Task<BannedWordList?> FindByWordAsync(string normalizedWord, CancellationToken cancellationToken = default) =>
            Task.FromResult(_words.FirstOrDefault(word =>
                string.Equals(word.Word, normalizedWord, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<string>> GetActiveWordsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_words.Where(word => word.IsActive).Select(word => word.Word).ToList());

        public Task AddAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default)
        {
            word.Id = _words.Count + 1;
            _words.Add(word);
            auditLog.EntityId = word.Id;
            Audits.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(BannedWordList word, AuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Audits.Add(auditLog);
            return Task.CompletedTask;
        }
    }

    private sealed class AuditLogRepositoryFake : IAuditLogRepository
    {
        public DateTime? LastFrom { get; private set; }
        public DateTime? LastToExclusive { get; private set; }
        public string? LastActorSearch { get; private set; }
        public AuditActionType? LastActionType { get; private set; }

        public Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> SearchAsync(
            DateTime? fromUtc, DateTime? toUtcExclusive, string? actorUserId, AuditActionType? actionType,
            string? entity, int page, int pageSize, CancellationToken cancellationToken = default, string? actorSearch = null)
        {
            LastFrom = fromUtc;
            LastToExclusive = toUtcExclusive;
            LastActorSearch = actorSearch;
            LastActionType = actionType;
            return Task.FromResult<(IReadOnlyList<AuditLog>, int)>(([], 0));
        }

        public Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuditLog?>(null);
    }
}
