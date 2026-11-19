using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Equipment.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class EquipmentTests
{
    private const string Member = "member-1", Staff = "staff-1", Manager = "manager-1";

    // ── Validators ───────────────────────────────────────────────

    [Fact]
    public void EquipmentValidatorRejectsFuturePurchaseDateAndNegativePrice()
    {
        var v = new EquipmentRequestValidator();
        Assert.True(v.Validate(new EquipmentRequest("Treadmill", 1, PurchasePrice: 1000)).IsValid);
        Assert.False(v.Validate(new EquipmentRequest("Treadmill", 1, PurchaseDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)))).IsValid);
        Assert.False(v.Validate(new EquipmentRequest("Treadmill", 1, PurchasePrice: -1)).IsValid);
        Assert.False(v.Validate(new EquipmentRequest("  ", 1)).IsValid);
        Assert.False(v.Validate(new EquipmentRequest("Treadmill", 1, DescriptionUrl: "javascript:alert(1)")).IsValid);
        Assert.False(v.Validate(new EquipmentRequest("Treadmill")).IsValid); // category is required
    }

    [Fact]
    public void CategoryValidatorRequiresAPrefixOfTwoToFiveLetters()
    {
        var v = new EquipmentCategoryRequestValidator();
        Assert.True(v.Validate(new EquipmentCategoryRequest("Cardio", CodePrefix: "tm")).IsValid);
        Assert.False(v.Validate(new EquipmentCategoryRequest("Cardio")).IsValid);
        Assert.False(v.Validate(new EquipmentCategoryRequest("Cardio", CodePrefix: "T")).IsValid);
        Assert.False(v.Validate(new EquipmentCategoryRequest("Cardio", CodePrefix: "TM1")).IsValid);
        Assert.False(v.Validate(new EquipmentCategoryRequest("Cardio", CodePrefix: "TOOLONG")).IsValid);
    }

    [Fact]
    public void RetiringRequiresANote()
    {
        var v = new EquipmentStatusRequestValidator();
        Assert.False(v.Validate(new EquipmentStatusRequest("Retired")).IsValid);
        Assert.True(v.Validate(new EquipmentStatusRequest("retired", "Broken frame")).IsValid);
        Assert.False(v.Validate(new EquipmentStatusRequest("Available")).IsValid);
    }

    [Fact]
    public void ReportValidatorChecksTypeAndDescription()
    {
        var v = new ReportEquipmentIssueRequestValidator();
        Assert.True(v.Validate(new ReportEquipmentIssueRequest(1, "Malfunction", "Belt slips")).IsValid);
        Assert.False(v.Validate(new ReportEquipmentIssueRequest(1, "Severe", "Belt slips")).IsValid);
        Assert.False(v.Validate(new ReportEquipmentIssueRequest(1, "Damage", " ")).IsValid);
        Assert.False(v.Validate(new ReportEquipmentIssueRequest(0, "Damage", "x")).IsValid);
    }

    [Fact]
    public void EquipmentListValidatorChecksBrandAndSort()
    {
        var v = new EquipmentListRequestValidator();
        Assert.True(v.Validate(new EquipmentListRequest(Brand: "Life Fitness", Sort: "purchase_date")).IsValid);
        Assert.True(v.Validate(new EquipmentListRequest(Sort: "CODE")).IsValid);
        Assert.True(v.Validate(new EquipmentListRequest()).IsValid);
        Assert.False(v.Validate(new EquipmentListRequest(Sort: "price")).IsValid);
        Assert.False(v.Validate(new EquipmentListRequest(Brand: new string('b', 101))).IsValid);
    }

    [Fact]
    public void IssueListValidatorChecksSearchAndSort()
    {
        var v = new EquipmentIssueListRequestValidator();
        Assert.True(v.Validate(new EquipmentIssueListRequest(Search: "TM-0001", Sort: "severity")).IsValid);
        Assert.True(v.Validate(new EquipmentIssueListRequest(Sort: "Newest")).IsValid);
        Assert.False(v.Validate(new EquipmentIssueListRequest(Sort: "oldest")).IsValid);
        Assert.False(v.Validate(new EquipmentIssueListRequest(Search: new string('s', 101))).IsValid);
    }

    // ── Equipment service ────────────────────────────────────────

    [Fact]
    public async Task CreateEquipmentStartsOperationalAndRejectsDuplicateCode()
    {
        var repo = new Repo();
        var cat = repo.SeedCategory("Treadmills", "TM");
        var service = new EquipmentService(repo, Mapper());

        var created = await service.CreateEquipmentAsync(new EquipmentRequest("Treadmill", cat.Id, Code: "TM-0001"), Manager, null);
        var duplicate = await service.CreateEquipmentAsync(new EquipmentRequest("Bike", cat.Id, Code: "tm-0001"), Manager, null);

        Assert.Equal(201, created.StatusCode);
        Assert.Equal("Operational", created.Data!.Status);
        Assert.Equal("EQUIPMENT_CODE_EXISTS", duplicate.ErrorCode);
        Assert.Contains(repo.Audits, a => a.AuditEntity == "Equipment" && a.ActionType == AuditActionType.Create);
    }

    [Fact]
    public async Task BlankCodeIsGeneratedFromTheCategoryPrefix()
    {
        var repo = new Repo();
        var cat = repo.SeedCategory("Treadmills", "TM");
        var service = new EquipmentService(repo, Mapper());

        var first = await service.CreateEquipmentAsync(new EquipmentRequest("Treadmill A", cat.Id), Manager, null);
        await service.CreateEquipmentAsync(new EquipmentRequest("Treadmill B", cat.Id, Code: "TM-0007"), Manager, null);
        var next = await service.CreateEquipmentAsync(new EquipmentRequest("Treadmill C", cat.Id, Code: "  "), Manager, null);

        Assert.Equal("TM-0001", first.Data!.Code);
        Assert.Equal("TM-0008", next.Data!.Code);
        var categories = await service.GetCategoriesAsync();
        Assert.Equal("TM-0009", Assert.Single(categories.Data!).NextCode);
    }

    [Fact]
    public async Task TypedCodeMustFollowTheCategoryFormat()
    {
        var repo = new Repo();
        var tm = repo.SeedCategory("Treadmills", "TM");
        var service = new EquipmentService(repo, Mapper());

        Assert.Equal("EQUIPMENT_CODE_INVALID_FORMAT",
            (await service.CreateEquipmentAsync(new EquipmentRequest("A", tm.Id, Code: "TM-12"), Manager, null)).ErrorCode);
        Assert.Equal("EQUIPMENT_CODE_INVALID_FORMAT",
            (await service.CreateEquipmentAsync(new EquipmentRequest("A", tm.Id, Code: "XD-0001"), Manager, null)).ErrorCode);
        Assert.Equal("TM-0012",
            (await service.CreateEquipmentAsync(new EquipmentRequest("A", tm.Id, Code: "tm-0012"), Manager, null)).Data!.Code);
    }

    [Fact]
    public async Task CategoryWithoutPrefixCannotTakeNewEquipmentButOldRecordsStillSave()
    {
        var repo = new Repo();
        var legacy = repo.SeedCategory("Old category");
        var old = repo.SeedEquipment(EquipmentStatus.Operational);
        old.CatalogCategoryId = legacy.Id;
        old.EquipmentCode = "OLD-1";
        var service = new EquipmentService(repo, Mapper());

        var create = await service.CreateEquipmentAsync(new EquipmentRequest("New", legacy.Id), Manager, null);
        var update = await service.UpdateEquipmentAsync(old.Id, new EquipmentRequest("Renamed", legacy.Id, Code: "OLD-1"), Manager, null);

        Assert.Equal("EQUIPMENT_CATEGORY_PREFIX_MISSING", create.ErrorCode);
        Assert.True(update.IsSuccess);
        Assert.Equal("OLD-1", update.Data!.Code);
    }

    [Fact]
    public async Task MovingEquipmentToAnotherCategoryRegeneratesABlankCode()
    {
        var repo = new Repo();
        var tm = repo.SeedCategory("Treadmills", "TM");
        var xd = repo.SeedCategory("Bikes", "XD");
        var service = new EquipmentService(repo, Mapper());
        var created = await service.CreateEquipmentAsync(new EquipmentRequest("Bike", tm.Id), Manager, null);

        var kept = await service.UpdateEquipmentAsync(created.Data!.Id, new EquipmentRequest("Bike", xd.Id, Code: "TM-0001"), Manager, null);
        var moved = await service.UpdateEquipmentAsync(created.Data.Id, new EquipmentRequest("Bike", xd.Id), Manager, null);

        Assert.Equal("EQUIPMENT_CODE_INVALID_FORMAT", kept.ErrorCode);
        Assert.Equal("XD-0001", moved.Data!.Code);
    }

    [Fact]
    public async Task CategoryPrefixIsUppercasedAndUnique()
    {
        var repo = new Repo();
        var service = new EquipmentService(repo, Mapper());

        var created = await service.CreateCategoryAsync(new EquipmentCategoryRequest("Treadmills", CodePrefix: " tm "), Manager, null);
        var duplicate = await service.CreateCategoryAsync(new EquipmentCategoryRequest("Running", CodePrefix: "TM"), Manager, null);

        Assert.Equal("TM", created.Data!.CodePrefix);
        Assert.Equal("TM-0001", created.Data.NextCode);
        Assert.Equal("EQUIPMENT_CATEGORY_PREFIX_EXISTS", duplicate.ErrorCode);
    }

    [Fact]
    public async Task CannotSetOperationalWhileAnIssueIsOpen()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Damaged);
        repo.SeedIssue(eq, EquipmentIssueStatus.Escalated);

        var result = await new EquipmentService(repo, Mapper())
            .ChangeStatusAsync(eq.Id, new EquipmentStatusRequest("Operational"), Manager, null);

        Assert.Equal("EQUIPMENT_HAS_OPEN_ISSUE", result.ErrorCode);
        Assert.Equal(EquipmentStatus.Damaged, eq.EquipmentStatus);
    }

    [Fact]
    public async Task CategoryInUseCannotBeDeleted()
    {
        var repo = new Repo();
        var category = repo.SeedCategory("Cardio");
        repo.SeedEquipment(EquipmentStatus.Operational).CatalogCategoryId = category.Id;

        var result = await new EquipmentService(repo, Mapper()).DeleteCategoryAsync(category.Id, Manager, null);

        Assert.Equal("EQUIPMENT_CATEGORY_IN_USE", result.ErrorCode);
        Assert.False(category.IsDeleted);
    }

    // ── Report (UC-71) ───────────────────────────────────────────

    [Fact]
    public async Task StaffCanReportAndIsNotNotifiedAboutTheirOwnReport()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);

        var result = await Issues(repo).ReportAsync(new ReportEquipmentIssueRequest(eq.Id, "Safety", "Loose bolt"), Staff, null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(Staff, result.Data!.ReportedByUserId);
        Assert.Empty(repo.Notifications); // the only Staff user is the reporter
    }

    [Fact]
    public async Task ReportCreatesReportedIssueAndNotifiesStaff()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);

        var result = await Issues(repo).ReportAsync(new ReportEquipmentIssueRequest(eq.Id, "malfunction", "Belt slips"), Member, null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal("Reported", result.Data!.Status);
        Assert.Equal("Malfunction", result.Data.IncidentType);
        var note = Assert.Single(repo.Notifications);
        Assert.Equal(Staff, note.RecipientUserId);
        Assert.Equal("EquipmentIssueUpdate", note.NotificationType);
    }

    [Fact]
    public async Task ReportRejectsRetiredEquipmentAndWarnsOnDuplicate()
    {
        var repo = new Repo();
        var retired = repo.SeedEquipment(EquipmentStatus.Retired);
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);
        repo.SeedIssue(eq, EquipmentIssueStatus.Reported);
        var service = Issues(repo);

        Assert.Equal("EQUIPMENT_RETIRED", (await service.ReportAsync(new(retired.Id, "Damage", "x"), Member, null)).ErrorCode);
        Assert.Equal("EQUIPMENT_ISSUE_DUPLICATE", (await service.ReportAsync(new(eq.Id, "Damage", "x"), Member, null)).ErrorCode);
        Assert.True((await service.ReportAsync(new(eq.Id, "Damage", "x", ConfirmDuplicate: true), Member, null)).IsSuccess);
    }

    // ── Staff (UC-72) ────────────────────────────────────────────

    [Fact]
    public async Task StaffResolveRequiresStartThenRecordsSafetyAndRestoresEquipment()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);
        var issue = repo.SeedIssue(eq, EquipmentIssueStatus.Reported);
        var service = Issues(repo);

        var tooEarly = await service.ResolveAsync(issue.Id, "Tightened belt", Staff, null);
        await service.StartHandlingAsync(issue.Id, Staff, null);
        var resolved = await service.ResolveAsync(issue.Id, "Tightened belt", Staff, null);

        Assert.Equal("EQUIPMENT_ISSUE_INVALID_STATE", tooEarly.ErrorCode);
        Assert.Equal("Resolved", resolved.Data!.Status);
        Assert.Equal(EquipmentStatus.Operational, eq.EquipmentStatus);
        Assert.Single(repo.SafetyConfirmations);
        Assert.Contains(repo.Notifications, n => n.RecipientUserId == Member);
    }

    [Fact]
    public async Task EscalateTakesEquipmentOutOfUseAndNotifiesManager()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);
        var issue = repo.SeedIssue(eq, EquipmentIssueStatus.Reported);

        var result = await Issues(repo).EscalateAsync(issue.Id, "Motor burnt", Staff, null);

        Assert.Equal("Escalated", result.Data!.Status);
        Assert.Equal(EquipmentStatus.Damaged, eq.EquipmentStatus);
        Assert.Contains(repo.Notifications, n => n.RecipientUserId == Manager);
        Assert.Contains(repo.Audits, a => a.AuditEntity == "Equipment" && a.NewValue!.StartsWith("Status=Damaged"));
    }

    [Fact]
    public async Task MarkSafeKeepsEquipmentStatus()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Operational);
        var issue = repo.SeedIssue(eq, EquipmentIssueStatus.Reported);

        var result = await Issues(repo).MarkSafeAsync(issue.Id, "Works fine", Staff, null);

        Assert.Equal("MarkedSafe", result.Data!.Status);
        Assert.Equal(EquipmentStatus.Operational, eq.EquipmentStatus);
        Assert.Single(repo.SafetyConfirmations);
    }

    // ── Manager repairs (UC-73) ──────────────────────────────────

    [Fact]
    public async Task RepairFlowFromEscalatedToOperational()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Damaged);
        var issue = repo.SeedIssue(eq, EquipmentIssueStatus.Escalated);
        var service = Issues(repo);

        var started = await service.StartRepairAsync(issue.Id, Manager, null);
        var staffBlocked = await service.ResolveAsync(issue.Id, "Fixed", Staff, null);
        var notSafe = await service.MarkNotSafeAsync(issue.Id, "Still sparks", Manager, null);
        var damagedAfterNotSafe = eq.EquipmentStatus;
        var confirmed = await service.ConfirmSafeAsync(issue.Id, "Motor replaced", Manager, null);

        Assert.Equal("InProgress", started.Data!.Status);
        Assert.Equal("EQUIPMENT_ISSUE_UNDER_REPAIR", staffBlocked.ErrorCode);
        Assert.Equal("InProgress", notSafe.Data!.Status);
        Assert.Equal(EquipmentStatus.Damaged, damagedAfterNotSafe);
        Assert.Equal("Resolved", confirmed.Data!.Status);
        Assert.Equal(EquipmentStatus.Operational, eq.EquipmentStatus);
    }

    [Fact]
    public async Task ConfirmSafeNeedsRepairStartedAndDisposeRetires()
    {
        var repo = new Repo();
        var eq = repo.SeedEquipment(EquipmentStatus.Damaged);
        var issue = repo.SeedIssue(eq, EquipmentIssueStatus.Escalated);
        var service = Issues(repo);

        var early = await service.ConfirmSafeAsync(issue.Id, "ok", Manager, null);
        var disposed = await service.DisposeAsync(issue.Id, "Beyond repair", Manager, null);

        Assert.Equal("EQUIPMENT_ISSUE_INVALID_STATE", early.ErrorCode);
        Assert.Equal("Resolved", disposed.Data!.Status);
        Assert.Equal(EquipmentStatus.Retired, eq.EquipmentStatus);
    }

    // ── Helpers ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null, EquipmentSort.Name)]
    [InlineData("code", EquipmentSort.Code)]
    [InlineData(" Newest ", EquipmentSort.Newest)]
    [InlineData("purchase_date", EquipmentSort.PurchaseDate)]
    public async Task EquipmentListPassesBrandAndSortToTheRepository(string? sort, EquipmentSort expected)
    {
        var repo = new Repo();
        await new EquipmentService(repo, Mapper()).GetEquipmentListAsync(new EquipmentListRequest(Brand: "  Life Fitness ", Sort: sort));

        Assert.Equal(expected, repo.LastEquipmentQuery!.Sort);
        Assert.Equal("Life Fitness", repo.LastEquipmentQuery.Brand);
    }

    [Fact]
    public async Task BlankBrandMeansNoBrandFilter()
    {
        var repo = new Repo();
        await new EquipmentService(repo, Mapper()).GetEquipmentListAsync(new EquipmentListRequest(Brand: "   "));
        Assert.Null(repo.LastEquipmentQuery!.Brand);
    }

    [Theory]
    [InlineData(null, EquipmentIssueSort.Newest)]
    [InlineData("severity", EquipmentIssueSort.Severity)]
    [InlineData("SEVERITY", EquipmentIssueSort.Severity)]
    public async Task IssueListPassesSearchAndSortToTheRepository(string? sort, EquipmentIssueSort expected)
    {
        var repo = new Repo();
        await Issues(repo).GetIssuesAsync(new EquipmentIssueListRequest(Search: " TM-0001 ", Sort: sort), null);

        Assert.Equal(expected, repo.LastIssueQuery!.Sort);
        Assert.Equal("TM-0001", repo.LastIssueQuery.Search);
    }

        private static EquipmentIssueService Issues(Repo repo) => new(repo, Mapper());

    private static IMapper Mapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(EquipmentMapping).Assembly);
        return new ServiceMapper(new NoServices(), config);
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class Repo : IEquipmentRepository
    {
        private int _nextId = 1;
        private readonly List<CatalogCategory> _categories = [];
        private readonly List<Equipment> _equipment = [];
        private readonly List<EquipmentIssueReport> _issues = [];
        public List<AuditLog> Audits { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public List<EquipmentSafetyConfirmation> SafetyConfirmations { get; } = [];

        public CatalogCategory SeedCategory(string name, string? prefix = null)
        {
            var c = new CatalogCategory { Id = _nextId++, CategoryName = name, CodePrefix = prefix };
            _categories.Add(c);
            return c;
        }

        public Equipment SeedEquipment(EquipmentStatus status)
        {
            var e = new Equipment { Id = _nextId++, GymId = 1, EquipmentName = "Machine " + _nextId, EquipmentStatus = status };
            _equipment.Add(e);
            return e;
        }

        public EquipmentIssueReport SeedIssue(Equipment equipment, EquipmentIssueStatus status)
        {
            var i = new EquipmentIssueReport
            {
                Id = _nextId++, EquipmentId = equipment.Id, Equipment = equipment, ReportedByUserId = Member,
                IncidentType = IncidentType.Malfunction, Description = "x", IssueStatus = status, ReportedAt = DateTime.UtcNow
            };
            _issues.Add(i);
            return i;
        }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            switch (entity)
            {
                case AuditLog a: Audits.Add(a); break;
                case Notification n: Notifications.Add(n); break;
                case EquipmentSafetyConfirmation s:
                    SafetyConfirmations.Add(s);
                    _issues.First(i => i.Id == s.EquipmentIssueReportId).SafetyConfirmations.Add(s);
                    break;
                case CatalogCategory c: _categories.Add(c); break;
                case Equipment e: _equipment.Add(e); break;
                case EquipmentIssueReport i:
                    i.Equipment = _equipment.First(e => e.Id == i.EquipmentId);
                    _issues.Add(i);
                    break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(roleName switch { "Staff" => [Staff], "Manager" => [Manager], _ => [] });

        public Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult(roleName == "Manager" && userId == Manager);

        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogCategory>>(_categories.Where(c => !c.IsDeleted).ToList());

        public Task<IReadOnlyDictionary<int, int>> CountEquipmentByCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(_equipment.Where(e => e.CatalogCategoryId != null)
                .GroupBy(e => e.CatalogCategoryId!.Value).ToDictionary(g => g.Key, g => g.Count()));

        public Task<CatalogCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));

        public Task<bool> CategoryNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_categories.Any(c => !c.IsDeleted && c.Id != excludingId &&
                string.Equals(c.CategoryName, name, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> CategoryPrefixExistsAsync(string prefix, int? excludingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_categories.Any(c => !c.IsDeleted && c.Id != excludingId && c.CodePrefix == prefix));

        public Task<int> GetMaxCodeNumberAsync(string prefix, CancellationToken cancellationToken = default) =>
            Task.FromResult(_equipment
                .Where(e => e.EquipmentCode != null && e.EquipmentCode.StartsWith(prefix + "-"))
                .Select(e => int.TryParse(e.EquipmentCode![(prefix.Length + 1)..], out var n) ? n : 0)
                .DefaultIfEmpty(0).Max());

        public Task<bool> CategoryInUseAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_equipment.Any(e => e.CatalogCategoryId == id));

        public EquipmentQuery? LastEquipmentQuery { get; private set; }
        public EquipmentIssueQuery? LastIssueQuery { get; private set; }

        public Task<(IReadOnlyList<Equipment> Items, int TotalCount)> SearchEquipmentAsync(
            EquipmentQuery query, CancellationToken cancellationToken = default)
        {
            LastEquipmentQuery = query;
            return Task.FromResult<(IReadOnlyList<Equipment>, int)>((_equipment, _equipment.Count));
        }

        public Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_equipment.Select(e => e.EquipmentBrand).OfType<string>().Distinct().Order().ToList());

        public Task<Equipment?> GetEquipmentAsync(int id, CancellationToken cancellationToken = default)
        {
            var e = _equipment.FirstOrDefault(x => x.Id == id);
            if (e is not null) e.CatalogCategory = _categories.FirstOrDefault(c => c.Id == e.CatalogCategoryId);
            return Task.FromResult(e);
        }

        public Task<bool> EquipmentCodeExistsAsync(string code, int? excludingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_equipment.Any(e => e.Id != excludingId &&
                string.Equals(e.EquipmentCode, code, StringComparison.OrdinalIgnoreCase)));

        public Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);

        public Task<bool> HasOpenIssueAsync(int equipmentId, int? excludingIssueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_issues.Any(i => i.EquipmentId == equipmentId && i.Id != excludingIssueId &&
                i.IssueStatus is EquipmentIssueStatus.Reported or EquipmentIssueStatus.InProgress or EquipmentIssueStatus.Escalated));

        public Task<(IReadOnlyList<EquipmentIssueReport> Items, int TotalCount)> SearchIssuesAsync(
            EquipmentIssueQuery query, CancellationToken cancellationToken = default)
        {
            LastIssueQuery = query;
            return Task.FromResult<(IReadOnlyList<EquipmentIssueReport>, int)>((_issues, _issues.Count));
        }

        public Task<EquipmentIssueReport?> GetIssueAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_issues.FirstOrDefault(i => i.Id == id));
    }
}
