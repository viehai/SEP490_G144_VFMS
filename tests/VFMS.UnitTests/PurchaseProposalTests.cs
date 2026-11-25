using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Procurement;
using VFMS.Contracts.Procurement.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class PurchaseProposalTests
{
    private const string Staff = "staff-1", OtherStaff = "staff-2", Manager = "manager-1";

    private static PurchaseProposalRequest Request(bool submit = true) => new(
        "Replace worn equipment",
        [new("Dumbbell set", 2), new("Yoga mats", 10, Reason: "Torn")],
        submit);

    // ── Validators (GBR-62) ──────────────────────────────────────

    [Fact]
    public void ValidatorRequiresDescriptionAndNamedItemsButNoPrice()
    {
        var v = new PurchaseProposalRequestValidator();
        Assert.True(v.Validate(Request()).IsValid);
        Assert.False(v.Validate(new PurchaseProposalRequest(" ", [new("A", 1)])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalRequest("x", [])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalRequest("x", [new("A", 0)])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalRequest("x", [new(" ", 1)])).IsValid);
    }

    [Fact]
    public void CompletionNeedsARealPriceForEachItemOnce()
    {
        var v = new PurchaseProposalCompleteRequestValidator();
        Assert.True(v.Validate(new PurchaseProposalCompleteRequest([new(1, 1_500_000m), new(2, 200_000m)])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalCompleteRequest([])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalCompleteRequest([new(1, 0m)])).IsValid);
        Assert.False(v.Validate(new PurchaseProposalCompleteRequest([new(1, 5m), new(1, 6m)])).IsValid);
    }

    // ── Staff (UC-74) ────────────────────────────────────────────

    [Fact]
    public async Task CreateSubmittedHasNoPriceYetAndNotifiesManagers()
    {
        var repo = new Repo();
        var result = await Service(repo).CreateAsync(Request(), Staff, null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal("Submitted", result.Data!.Status);
        Assert.Null(result.Data.TotalCost);
        Assert.All(result.Data.Items, item => Assert.Null(item.UnitPrice));
        Assert.Matches(@"^PP-\d{8}-\d{6}$", result.Data.ProposalNumber);
        Assert.NotNull(result.Data.SubmittedAt);
        Assert.Equal(Manager, Assert.Single(repo.Notifications).RecipientUserId);
    }

    [Fact]
    public async Task DraftIsNotSentAndCanBeEditedThenSubmitted()
    {
        var repo = new Repo();
        var service = Service(repo);
        var draft = await service.CreateAsync(Request(submit: false), Staff, null);
        Assert.Equal("Draft", draft.Data!.Status);
        Assert.Null(draft.Data.SubmittedAt);
        Assert.Empty(repo.Notifications);

        var edited = await service.UpdateDraftAsync(draft.Data.Id,
            new PurchaseProposalRequest("Only mats", [new("Yoga mats", 5)], Submit: false), Staff, null);
        Assert.Equal(5, Assert.Single(edited.Data!.Items).Quantity);

        var sent = await service.SubmitAsync(draft.Data.Id, Staff, null);
        Assert.Equal("Submitted", sent.Data!.Status);
        Assert.Single(repo.Notifications);
    }

    [Fact]
    public async Task SubmittedProposalCannotBeEditedOrSentAgain()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);

        Assert.Equal("PROPOSAL_INVALID_STATE", (await service.UpdateDraftAsync(created.Data!.Id, Request(), Staff, null)).ErrorCode);
        Assert.Equal("PROPOSAL_INVALID_STATE", (await service.SubmitAsync(created.Data.Id, Staff, null)).ErrorCode);
    }

    [Fact]
    public async Task StaffCannotSeeOrEditAnotherStaffsProposal()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(submit: false), Staff, null);

        Assert.Equal(404, (await service.GetAsync(created.Data!.Id, OtherStaff)).StatusCode);
        Assert.Equal(404, (await service.UpdateDraftAsync(created.Data.Id, Request(), OtherStaff, null)).StatusCode);
    }

    [Fact]
    public async Task UnknownRelatedEquipmentIsRejected()
    {
        var result = await Service(new Repo()).CreateAsync(
            new PurchaseProposalRequest("x", [new("Belt", 1, EquipmentId: 999)]), Staff, null);
        Assert.Equal("PROPOSAL_EQUIPMENT_NOT_FOUND", result.ErrorCode);
    }

    // ── Manager (UC-75, GBR-68) ──────────────────────────────────

    [Fact]
    public async Task ManagerCannotSeeUnsentDrafts()
    {
        var repo = new Repo();
        var service = Service(repo);
        var draft = await service.CreateAsync(Request(submit: false), Staff, null);

        Assert.Equal(404, (await service.GetAsync(draft.Data!.Id, null)).StatusCode);
        Assert.Equal(404, (await service.ApproveAsync(draft.Data.Id, null, Manager, null)).StatusCode);
    }

    [Fact]
    public async Task ApproveRecordsReviewAndOnlyOnce()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);

        var approved = await service.ApproveAsync(created.Data!.Id, null, Manager, null);
        var again = await service.RejectAsync(created.Data.Id, "Too late", Manager, null);

        Assert.Equal("Approved", approved.Data!.Status);
        var review = Assert.Single(approved.Data.Reviews);
        Assert.Equal("Approved", review.Action);
        Assert.Equal("PROPOSAL_INVALID_STATE", again.ErrorCode);
        Assert.Contains(repo.Notifications, n => n.RecipientUserId == Staff && n.Title == "Purchase proposal approved");
    }

    [Fact]
    public async Task RejectAndRevisionRequireANote()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);

        Assert.Equal("PROPOSAL_NOTE_REQUIRED", (await service.RejectAsync(created.Data!.Id, " ", Manager, null)).ErrorCode);
        Assert.Equal("PROPOSAL_NOTE_REQUIRED", (await service.RequestRevisionAsync(created.Data.Id, null, Manager, null)).ErrorCode);
        var rejected = await service.RejectAsync(created.Data.Id, "Over budget", Manager, null);
        Assert.Equal("Rejected", rejected.Data!.Status);
        Assert.Equal("Over budget", rejected.Data.Reviews.Single().ReviewNote);
    }

    [Fact]
    public async Task RevisionReturnsToDraftAndCanBeResent()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);

        var revision = await service.RequestRevisionAsync(created.Data!.Id, "Add quotes", Manager, null);
        var managerCanStillOpen = await service.GetAsync(created.Data.Id, null);
        var resent = await service.UpdateDraftAsync(created.Data.Id, Request(), Staff, null);

        Assert.Equal("Draft", revision.Data!.Status);
        Assert.True(managerCanStillOpen.IsSuccess);
        Assert.Equal("Submitted", resent.Data!.Status);
        Assert.Equal("RequestRevision", resent.Data.Reviews.Single().Action);
    }

    [Fact]
    public async Task CompleteOnlyAfterApprovalWithTheRealPrices()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);
        var items = created.Data!.Items.ToArray();
        var prices = new PurchaseProposalCompleteRequest([new(items[0].Id, 1_500_000m), new(items[1].Id, 200_000m)]);

        var early = await service.CompleteAsync(created.Data.Id, prices, Manager, null);
        await service.ApproveAsync(created.Data.Id, "OK", Manager, null);
        var done = await service.CompleteAsync(created.Data.Id, prices, Manager, null);

        Assert.Equal("PROPOSAL_INVALID_STATE", early.ErrorCode);
        Assert.Equal("Completed", done.Data!.Status);
        Assert.Equal(5_000_000m, done.Data.TotalCost);              // 2×1.5M + 10×200k
        Assert.Equal(3_000_000m, done.Data.Items.First().LineTotal);
        Assert.Contains("Total=5000000", repo.Audits.Last().NewValue);
    }

    [Fact]
    public async Task CompleteNeedsAPriceForEveryItemOfThisProposal()
    {
        var repo = new Repo();
        var service = Service(repo);
        var created = await service.CreateAsync(Request(), Staff, null);
        await service.ApproveAsync(created.Data!.Id, null, Manager, null);
        var first = created.Data.Items.First().Id;

        var missing = await service.CompleteAsync(created.Data.Id, new([new(first, 10m)]), Manager, null);
        var foreign = await service.CompleteAsync(created.Data.Id, new([new(first, 10m), new(9_999, 10m)]), Manager, null);

        Assert.Equal("PROPOSAL_PRICES_REQUIRED", missing.ErrorCode);
        Assert.Equal("PROPOSAL_PRICES_REQUIRED", foreign.ErrorCode);
        Assert.Equal("Approved", (await service.GetAsync(created.Data.Id, null)).Data!.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────

    [Fact]
    public async Task StaffCountsTheirOwnProposalsAndTheManagerSeesNoDrafts()
    {
        var repo = new Repo();
        var service = Service(repo);
        await service.CreateAsync(Request(submit: false), Staff, null);
        await service.CreateAsync(Request(), Staff, null);
        await service.CreateAsync(Request(), OtherStaff, null);

        var mine = (await service.GetMyCountsAsync(Staff)).Data!;
        var review = (await service.GetReviewCountsAsync()).Data!;

        Assert.Equal(1, mine.Counts["Draft"]);
        Assert.Equal(1, mine.Counts["Submitted"]);
        Assert.Equal(2, mine.Total);
        Assert.False(review.Counts.ContainsKey("Draft"));
        Assert.Equal(2, review.Counts["Submitted"]);
        Assert.Equal(2, review.Total);
    }

    private static PurchaseProposalService Service(Repo repo)
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(ProcurementMapping).Assembly);
        return new PurchaseProposalService(repo, new ServiceMapper(new NoServices(), config));
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class Repo : IPurchaseProposalRepository
    {
        private int _nextId = 1;
        private readonly List<PurchaseProposal> _proposals = [];
        public List<Notification> Notifications { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            switch (entity)
            {
                case PurchaseProposal p:
                    foreach (var item in p.Items) if (item.Id == 0) item.Id = _nextId++;
                    _proposals.Add(p);
                    break;
                case PurchaseProposalItem i: _proposals.First(p => p.Id == i.PurchaseProposalId).Items.Add(i); break;
                case PurchaseProposalReview r: _proposals.First(p => p.Id == r.PurchaseProposalId).Reviews.Add(r); break;
                case Notification n: Notifications.Add(n); break;
                case AuditLog a: Audits.Add(a); break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(roleName == "Manager" ? [Manager] : []);

        public Task<IReadOnlyDictionary<PurchaseProposalStatus, int>> CountByStatusAsync(
            string? proposedByUserId, bool excludeDrafts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<PurchaseProposalStatus, int>>(_proposals
                .Where(p => proposedByUserId == null || p.ProposedByUserId == proposedByUserId)
                .Where(p => !excludeDrafts || p.ProposalStatus != PurchaseProposalStatus.Draft)
                .GroupBy(p => p.ProposalStatus).ToDictionary(g => g.Key, g => g.Count()));

        public Task<(IReadOnlyList<PurchaseProposal> Items, int TotalCount)> SearchAsync(
            string? proposedByUserId, PurchaseProposalStatus? status, bool excludeDrafts, string? search,
            int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<PurchaseProposal>, int)>((_proposals, _proposals.Count));

        public Task<PurchaseProposal?> GetAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_proposals.FirstOrDefault(p => p.Id == id));

        public Task<bool> ProposalNumberExistsAsync(string proposalNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(_proposals.Any(p => p.ProposalNumber == proposalNumber));

        public Task<IReadOnlySet<int>> GetExistingEquipmentIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }
}
