using System.Security.Cryptography;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Procurement;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-74 Manage Purchase Proposals (Staff) and UC-75 Approve Purchase Request (Manager).
/// GBR-62 (complete, costed proposal), GBR-68 (decide only while Submitted, note required to reject/revise).
/// Flow: Draft → Submitted → Approved → Completed, or Submitted → Rejected, or Submitted → Draft (revision requested).
/// </summary>
public sealed class PurchaseProposalService : IPurchaseProposalService
{
    private const string Entity = nameof(PurchaseProposal);
    private const string ManagerRole = "Manager";

    private readonly IPurchaseProposalRepository _repository;
    private readonly IMapper _mapper;

    public PurchaseProposalService(IPurchaseProposalRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    // ── Staff (UC-74) ────────────────────────────────────────────

    public Task<Result<PagedResponse<PurchaseProposalResponse>>> GetMyProposalsAsync(
        PurchaseProposalListRequest request, string staffUserId, CancellationToken cancellationToken = default) =>
        SearchAsync(request, staffUserId, excludeDrafts: false, cancellationToken);

    public async Task<Result<PurchaseProposalResponse>> CreateAsync(
        PurchaseProposalRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var check = await CheckEquipmentAsync(request, cancellationToken);
        if (check is not null)
            return check;

        var now = DateTime.UtcNow;
        var proposal = new PurchaseProposal
        {
            ProposedByUserId = staffUserId,
            ProposalNumber = await NewProposalNumberAsync(now, cancellationToken),
            Description = request.Description.Trim(),
            ProposalStatus = request.Submit ? PurchaseProposalStatus.Submitted : PurchaseProposalStatus.Draft,
            SubmittedAt = now,
            Items = BuildItems(request).ToList()
        };
        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Create, Entity, ipAddress,
            newValue: $"Number={proposal.ProposalNumber}; Status={proposal.ProposalStatus}; Items={proposal.Items.Count}; Total={Total(proposal)}");
        var notifications = request.Submit ? await NotifyManagersAsync(proposal, staffUserId, cancellationToken) : null;

        await AuditedWriter.SaveNewAsync(_repository, proposal, audit, notifications, cancellationToken);
        return await Reload(proposal.Id, 201, cancellationToken);
    }

    public async Task<Result<PurchaseProposalResponse>> UpdateDraftAsync(
        int id, PurchaseProposalRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var proposal = await _repository.GetAsync(id, cancellationToken);
        if (proposal is null || proposal.ProposedByUserId != staffUserId)
            return NotFound();
        if (proposal.ProposalStatus != PurchaseProposalStatus.Draft)
            return InvalidState(proposal, "Only Draft proposals can be edited.");
        var check = await CheckEquipmentAsync(request, cancellationToken);
        if (check is not null)
            return check;

        var before = $"Status={proposal.ProposalStatus}; Items={proposal.Items.Count}; Total={Total(proposal)}";
        foreach (var item in proposal.Items)
            item.IsDeleted = true; // soft delete; replaced by the new item list
        foreach (var item in BuildItems(request))
        {
            item.PurchaseProposalId = proposal.Id;
            _repository.Add(item);
        }
        proposal.Description = request.Description.Trim();

        IReadOnlyCollection<Notification>? notifications = null;
        if (request.Submit)
        {
            proposal.ProposalStatus = PurchaseProposalStatus.Submitted;
            proposal.SubmittedAt = DateTime.UtcNow;
            notifications = await NotifyManagersAsync(proposal, staffUserId, cancellationToken);
        }

        var newItems = request.Items;
        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Update, Entity, ipAddress, before,
            $"Status={proposal.ProposalStatus}; Items={newItems.Count}; Total={newItems.Sum(i => i.Quantity * i.EstimatedUnitPrice)}");
        await AuditedWriter.SaveChangedAsync(_repository, proposal, audit, notifications, cancellationToken);
        return await Reload(proposal.Id, 200, cancellationToken);
    }

    public async Task<Result<PurchaseProposalResponse>> SubmitAsync(
        int id, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var proposal = await _repository.GetAsync(id, cancellationToken);
        if (proposal is null || proposal.ProposedByUserId != staffUserId)
            return NotFound();
        if (proposal.ProposalStatus != PurchaseProposalStatus.Draft)
            return InvalidState(proposal, "Only Draft proposals can be sent.");
        if (proposal.Items.Count == 0 || string.IsNullOrWhiteSpace(proposal.Description))
            return Result<PurchaseProposalResponse>.Failure(
                "A proposal needs a description and at least one item before it can be sent.", "PROPOSAL_INCOMPLETE");

        proposal.ProposalStatus = PurchaseProposalStatus.Submitted;
        proposal.SubmittedAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={PurchaseProposalStatus.Draft}", $"Status={proposal.ProposalStatus}");
        var notifications = await NotifyManagersAsync(proposal, staffUserId, cancellationToken);
        await AuditedWriter.SaveChangedAsync(_repository, proposal, audit, notifications, cancellationToken);
        return await Reload(proposal.Id, 200, cancellationToken);
    }

    public async Task<Result<PurchaseProposalResponse>> GetAsync(
        int id, string? onlyProposedByUserId, CancellationToken cancellationToken = default)
    {
        var proposal = await _repository.GetAsync(id, cancellationToken);
        if (proposal is null || onlyProposedByUserId is not null && proposal.ProposedByUserId != onlyProposedByUserId)
            return NotFound();
        // Managers never see another person's unsent draft.
        if (onlyProposedByUserId is null && proposal.ProposalStatus == PurchaseProposalStatus.Draft &&
            !proposal.Reviews.Any())
            return NotFound();
        return Result<PurchaseProposalResponse>.Success(_mapper.Map<PurchaseProposalResponse>(proposal));
    }

    // ── Manager (UC-75) ──────────────────────────────────────────

    public Task<Result<PagedResponse<PurchaseProposalResponse>>> GetForReviewAsync(
        PurchaseProposalListRequest request, CancellationToken cancellationToken = default) =>
        SearchAsync(request, null, excludeDrafts: true, cancellationToken);

    public Task<Result<PurchaseProposalResponse>> ApproveAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        DecideAsync(id, PurchaseProposalReviewAction.Approved, PurchaseProposalStatus.Approved, note, noteRequired: false,
            managerUserId, ipAddress, cancellationToken);

    public Task<Result<PurchaseProposalResponse>> RejectAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        DecideAsync(id, PurchaseProposalReviewAction.Rejected, PurchaseProposalStatus.Rejected, note, noteRequired: true,
            managerUserId, ipAddress, cancellationToken);

    public Task<Result<PurchaseProposalResponse>> RequestRevisionAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        DecideAsync(id, PurchaseProposalReviewAction.RequestRevision, PurchaseProposalStatus.Draft, note, noteRequired: true,
            managerUserId, ipAddress, cancellationToken);

    public async Task<Result<PurchaseProposalResponse>> CompleteAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var proposal = await _repository.GetAsync(id, cancellationToken);
        if (proposal is null || proposal.ProposalStatus == PurchaseProposalStatus.Draft && !proposal.Reviews.Any())
            return NotFound();
        if (proposal.ProposalStatus != PurchaseProposalStatus.Approved)
            return InvalidState(proposal, "Only Approved proposals can be marked completed.");

        proposal.ProposalStatus = PurchaseProposalStatus.Completed;
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={PurchaseProposalStatus.Approved}", $"Status={proposal.ProposalStatus}");
        await AuditedWriter.SaveChangedAsync(_repository, proposal, audit, null, cancellationToken);
        return await Reload(proposal.Id, 200, cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<Result<PurchaseProposalResponse>> DecideAsync(
        int id, PurchaseProposalReviewAction action, PurchaseProposalStatus newStatus, string? note, bool noteRequired,
        string managerUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        if (noteRequired && string.IsNullOrWhiteSpace(note))
            return Result<PurchaseProposalResponse>.Failure(
                action == PurchaseProposalReviewAction.Rejected
                    ? "A rejection reason is required."
                    : "A note explaining the requested revision is required.",
                "PROPOSAL_NOTE_REQUIRED");

        var proposal = await _repository.GetAsync(id, cancellationToken);
        if (proposal is null || proposal.ProposalStatus == PurchaseProposalStatus.Draft && !proposal.Reviews.Any())
            return NotFound();
        // GBR-68: decisions only while Submitted (covers a proposal processed by someone else meanwhile).
        if (proposal.ProposalStatus != PurchaseProposalStatus.Submitted)
            return InvalidState(proposal, "This proposal has already been processed.");

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        _repository.Add(new PurchaseProposalReview
        {
            PurchaseProposalId = proposal.Id,
            ReviewedByUserId = managerUserId,
            Action = action,
            ReviewNote = cleanNote,
            ReviewedAt = DateTime.UtcNow
        });
        var before = proposal.ProposalStatus;
        proposal.ProposalStatus = newStatus;

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={before}", $"Status={newStatus}; Decision={action}; Note={cleanNote}");
        var title = action switch
        {
            PurchaseProposalReviewAction.Approved => "Purchase proposal approved",
            PurchaseProposalReviewAction.Rejected => "Purchase proposal rejected",
            _ => "Purchase proposal needs revision"
        };
        var notifications = proposal.ProposedByUserId == managerUserId
            ? null
            : new[]
            {
                AuditedWriter.Notify(proposal.ProposedByUserId, managerUserId, title,
                    $"{proposal.ProposalNumber}{(cleanNote is null ? "" : ": " + cleanNote)}", Entity, NotificationType.SystemAlert)
            };
        await AuditedWriter.SaveChangedAsync(_repository, proposal, audit, notifications, cancellationToken);
        return await Reload(proposal.Id, 200, cancellationToken);
    }

    private async Task<Result<PagedResponse<PurchaseProposalResponse>>> SearchAsync(
        PurchaseProposalListRequest request, string? proposedByUserId, bool excludeDrafts, CancellationToken cancellationToken)
    {
        PurchaseProposalStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out PurchaseProposalStatus parsed))
                return Result<PagedResponse<PurchaseProposalResponse>>.Failure("Status filter is invalid.", "PROPOSAL_INVALID_STATUS");
            status = parsed;
        }

        var (items, total) = await _repository.SearchAsync(
            proposedByUserId, status, excludeDrafts, EquipmentService.Clean(request.Search),
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<PurchaseProposalResponse>>.Success(new PagedResponse<PurchaseProposalResponse>(
            request.Page, request.PageSize, total, items.Select(_mapper.Map<PurchaseProposalResponse>).ToArray()));
    }

    private async Task<Result<PurchaseProposalResponse>?> CheckEquipmentAsync(
        PurchaseProposalRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Items.Where(i => i.EquipmentId.HasValue).Select(i => i.EquipmentId!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return null;
        var existing = await _repository.GetExistingEquipmentIdsAsync(ids, cancellationToken);
        var missing = ids.Where(id => !existing.Contains(id)).ToArray();
        return missing.Length == 0
            ? null
            : Result<PurchaseProposalResponse>.Failure(
                $"Related equipment was not found: {string.Join(", ", missing)}.", "PROPOSAL_EQUIPMENT_NOT_FOUND");
    }

    private static IEnumerable<PurchaseProposalItem> BuildItems(PurchaseProposalRequest request) =>
        request.Items.Select(i => new PurchaseProposalItem
        {
            ItemName = i.ItemName.Trim(),
            EquipmentId = i.EquipmentId,
            Quantity = i.Quantity,
            EstimatedUnitPrice = i.EstimatedUnitPrice,
            Reason = EquipmentService.Clean(i.Reason)
        });

    private async Task<string> NewProposalNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = $"PP-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
            if (!await _repository.ProposalNumberExistsAsync(number, cancellationToken))
                return number;
        }
        return $"PP-{now:yyyyMMddHHmmssfff}";
    }

    private async Task<IReadOnlyCollection<Notification>> NotifyManagersAsync(
        PurchaseProposal proposal, string staffUserId, CancellationToken cancellationToken)
    {
        var managers = await _repository.GetActiveUserIdsInRoleAsync(ManagerRole, cancellationToken);
        return managers
            .Where(id => id != staffUserId)
            .Select(id => AuditedWriter.Notify(id, staffUserId, "New purchase proposal",
                $"{proposal.ProposalNumber}: {Total(proposal):N0} VND", Entity, NotificationType.SystemAlert))
            .ToArray();
    }

    private static decimal Total(PurchaseProposal proposal) =>
        proposal.Items.Where(i => !i.IsDeleted).Sum(i => i.Quantity * i.EstimatedUnitPrice);

    private async Task<Result<PurchaseProposalResponse>> Reload(int id, int statusCode, CancellationToken cancellationToken)
    {
        var proposal = await _repository.GetAsync(id, cancellationToken);
        return proposal is null
            ? NotFound()
            : Result<PurchaseProposalResponse>.Success(_mapper.Map<PurchaseProposalResponse>(proposal), statusCode);
    }

    private static Result<PurchaseProposalResponse> InvalidState(PurchaseProposal proposal, string message) =>
        Result<PurchaseProposalResponse>.Failure(
            $"{message} Current status: {proposal.ProposalStatus}.", "PROPOSAL_INVALID_STATE", 409);

    private static Result<PurchaseProposalResponse> NotFound() =>
        Result<PurchaseProposalResponse>.Failure("Purchase proposal was not found.", "PROPOSAL_NOT_FOUND", 404);
}
