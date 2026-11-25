using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Operations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-71 Report Equipment Issue (Member or Staff), UC-72 Handle Equipment Issues (Staff), UC-73 Manage Equipment Repairs (Manager).
/// GBR-53 (registered, non-retired equipment), GBR-54 (definite outcome), GBR-55 (safe before Operational), GBR-56 (audit).
/// </summary>
/// <remarks>
/// Status flow: InProgress (Staff handle it as soon as it is reported) → Resolved, or → Escalated (Manager repairs
/// or retires the equipment) → Resolved.
/// </remarks>
public sealed class EquipmentIssueService : IEquipmentIssueService
{
    private const string IssueEntity = nameof(EquipmentIssueReport);
    private const string EquipmentEntity = nameof(Equipment);
    private const string ManagerRole = "Manager";
    private const string StaffRole = "Staff";

    private readonly IEquipmentRepository _repository;
    private readonly IMapper _mapper;

    public EquipmentIssueService(IEquipmentRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    // ── Report (UC-71) ───────────────────────────────────────────

    public async Task<Result<EquipmentIssueResponse>> ReportAsync(
        ReportEquipmentIssueRequest request, string reporterUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!EquipmentService.TryParse(request.IncidentType, out IncidentType incidentType))
            return Result<EquipmentIssueResponse>.Failure("Incident type is invalid.", "EQUIPMENT_ISSUE_INVALID_TYPE");

        var equipment = await _repository.GetEquipmentAsync(request.EquipmentId, cancellationToken);
        if (equipment is null)
            return Result<EquipmentIssueResponse>.Failure("Equipment not found in the system.", "EQUIPMENT_NOT_FOUND", 404);
        if (equipment.EquipmentStatus == EquipmentStatus.Retired)
            return Result<EquipmentIssueResponse>.Failure(
                "This equipment is retired and cannot be reported.", "EQUIPMENT_RETIRED", 409);
        if (!request.ConfirmDuplicate && await _repository.HasOpenIssueAsync(equipment.Id, null, cancellationToken))
            return Result<EquipmentIssueResponse>.Failure(
                "A similar issue for this equipment was reported recently. Resend with confirmDuplicate=true to report anyway.",
                "EQUIPMENT_ISSUE_DUPLICATE", 409);

        var issue = new EquipmentIssueReport
        {
            EquipmentId = equipment.Id,
            ReportedByUserId = reporterUserId,
            IncidentType = incidentType,
            Description = request.Description.Trim(),
            ImageUrl = EquipmentService.Clean(request.ImageUrl),
            IssueStatus = EquipmentIssueStatus.InProgress,
            ReportedAt = DateTime.UtcNow
        };
        // A Safety report takes a working machine out of use straight away, so nobody uses it before Staff check it.
        // Closing the issue puts the machine back to Operational.
        var takenOutOfUse = incidentType == IncidentType.Safety && equipment.EquipmentStatus == EquipmentStatus.Operational;
        if (takenOutOfUse)
            equipment.EquipmentStatus = EquipmentStatus.Damaged;
        var audit = AuditedWriter.Audit(reporterUserId, AuditActionType.Create, IssueEntity, ipAddress,
            newValue: $"EquipmentId={equipment.Id}; Type={incidentType}; Status={issue.IssueStatus}"
                      + (takenOutOfUse ? $"; EquipmentStatus={EquipmentStatus.Damaged}" : ""));
        var notifications = await NotifyRoleAsync(StaffRole, reporterUserId, "New equipment issue",
            $"{equipment.EquipmentName}: {incidentType} reported.", cancellationToken);

        await AuditedWriter.SaveNewAsync(_repository, issue, audit, notifications, cancellationToken);
        return await Reload(issue.Id, 201, cancellationToken);
    }

    // ── Queries ──────────────────────────────────────────────────

    public async Task<Result<PagedResponse<EquipmentIssueResponse>>> GetIssuesAsync(
        EquipmentIssueListRequest request, string? onlyReportedByUserId, CancellationToken cancellationToken = default)
    {
        EquipmentIssueStatus? status = null;
        var openOnly = string.Equals(request.Status?.Trim(), StatusFilters.OpenIssues, StringComparison.OrdinalIgnoreCase);
        if (!openOnly && !string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out EquipmentIssueStatus parsed))
                return Result<PagedResponse<EquipmentIssueResponse>>.Failure("Status filter is invalid.", "EQUIPMENT_ISSUE_INVALID_STATUS");
            status = parsed;
        }

        var sort = string.Equals(request.Sort?.Trim(), "severity", StringComparison.OrdinalIgnoreCase)
            ? EquipmentIssueSort.Severity : EquipmentIssueSort.Newest;
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var (items, total) = await _repository.SearchIssuesAsync(
            new EquipmentIssueQuery(status, request.EquipmentId, onlyReportedByUserId, search, sort, request.Page, request.PageSize, openOnly),
            cancellationToken);
        return Result<PagedResponse<EquipmentIssueResponse>>.Success(new PagedResponse<EquipmentIssueResponse>(
            request.Page, request.PageSize, total, items.Select(_mapper.Map<EquipmentIssueResponse>).ToArray()));
    }

    public async Task<Result<StatusCountsResponse>> GetIssueCountsAsync(CancellationToken cancellationToken = default) =>
        Result<StatusCountsResponse>.Success(StatusCounts.From(await _repository.CountIssuesByStatusAsync(null, cancellationToken)));

    public async Task<Result<EquipmentIssueResponse>> GetIssueAsync(
        int id, string? onlyReportedByUserId, CancellationToken cancellationToken = default)
    {
        var issue = await _repository.GetIssueAsync(id, cancellationToken);
        if (issue is null || onlyReportedByUserId is not null && issue.ReportedByUserId != onlyReportedByUserId)
            return NotFound();
        return Result<EquipmentIssueResponse>.Success(_mapper.Map<EquipmentIssueResponse>(issue));
    }

    // ── Staff (UC-72) ────────────────────────────────────────────

    /// <summary>Staff fixed a small problem themselves (or found nothing wrong): the issue is done and the equipment is back in use.</summary>
    public Task<Result<EquipmentIssueResponse>> ResolveAsync(
        int id, string note, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        Act(id, staffUserId, ipAddress, cancellationToken, async issue =>
        {
            if (issue.IssueStatus != EquipmentIssueStatus.InProgress)
                return InvalidState(issue, "Only issues Staff are handling can be closed by Staff.");
            Close(issue, note, staffUserId);
            await BackInUseAsync(issue, cancellationToken);
            return await Done(NotifyReporter(issue, staffUserId, "Your equipment report was resolved", note));
        });

    /// <summary>Too big for Staff: the Manager takes over and the equipment is out of use until repaired.</summary>
    public Task<Result<EquipmentIssueResponse>> EscalateAsync(
        int id, string note, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        Act(id, staffUserId, ipAddress, cancellationToken, async issue =>
        {
            if (issue.IssueStatus != EquipmentIssueStatus.InProgress)
                return InvalidState(issue, "Only issues Staff are handling can be passed to the Manager.");
            issue.IssueStatus = EquipmentIssueStatus.Escalated;
            issue.HandledByUserId = staffUserId;
            issue.ResolutionNote = "Chuyển quản lý: " + note.Trim();
            if (issue.Equipment.EquipmentStatus != EquipmentStatus.Retired)
                issue.Equipment.EquipmentStatus = EquipmentStatus.Damaged; // out of use until repaired
            var notify = await NotifyRoleAsync(ManagerRole, staffUserId, "Equipment issue escalated",
                $"{issue.Equipment.EquipmentName}: {note.Trim()}", cancellationToken);
            return await Done(notify);
        });

    // ── Manager (UC-73) ──────────────────────────────────────────

    /// <summary>The Manager had the equipment repaired and checked it is safe: the issue is done and the equipment is back in use.</summary>
    public Task<Result<EquipmentIssueResponse>> ConfirmSafeAsync(
        int id, string note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        Act(id, managerUserId, ipAddress, cancellationToken, async issue =>
        {
            if (issue.IssueStatus != EquipmentIssueStatus.Escalated)
                return InvalidState(issue, "Only issues passed to the Manager can be closed as repaired.");
            Close(issue, note, managerUserId);
            await BackInUseAsync(issue, cancellationToken);
            return await Done(NotifyReporter(issue, managerUserId, "Reported equipment repaired", note));
        });

    /// <summary>Not worth repairing: the equipment is retired and the issue is done.</summary>
    public Task<Result<EquipmentIssueResponse>> DisposeAsync(
        int id, string note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        Act(id, managerUserId, ipAddress, cancellationToken, async issue =>
        {
            if (issue.IssueStatus != EquipmentIssueStatus.Escalated)
                return InvalidState(issue, "Only issues passed to the Manager can lead to retiring the equipment.");
            issue.IssueStatus = EquipmentIssueStatus.Resolved;
            issue.HandledByUserId = managerUserId;
            issue.ResolvedAt = DateTime.UtcNow;
            issue.ResolutionNote = "Thanh lý: " + note.Trim();
            issue.Equipment.EquipmentStatus = EquipmentStatus.Retired;
            return await Done(NotifyReporter(issue, managerUserId, "Reported equipment retired", note));
        });

    // ── Plumbing ─────────────────────────────────────────────────

    private sealed record Outcome(Result<EquipmentIssueResponse>? Error, IReadOnlyCollection<Notification> Notifications);

    /// <summary>Loads the issue, applies <paramref name="change"/>, then saves it with audit rows for the issue and equipment.</summary>
    private async Task<Result<EquipmentIssueResponse>> Act(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken,
        Func<EquipmentIssueReport, Task<Outcome>> change)
    {
        var issue = await _repository.GetIssueAsync(id, cancellationToken);
        if (issue is null)
            return NotFound();

        var issueBefore = $"Status={issue.IssueStatus}; HandledBy={issue.HandledByUserId}";
        var equipmentBefore = issue.Equipment.EquipmentStatus;

        var outcome = await change(issue);
        if (outcome.Error is not null)
            return outcome.Error;

        if (issue.Equipment.EquipmentStatus != equipmentBefore)
        {
            var equipmentAudit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, EquipmentEntity, ipAddress,
                $"Status={equipmentBefore}", $"Status={issue.Equipment.EquipmentStatus}; IssueId={issue.Id}");
            equipmentAudit.EntityId = issue.EquipmentId;
            _repository.Add(equipmentAudit);
        }
        var issueAudit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, IssueEntity, ipAddress, issueBefore,
            $"Status={issue.IssueStatus}; HandledBy={issue.HandledByUserId}; Note={issue.ResolutionNote}");
        await AuditedWriter.SaveChangedAsync(_repository, issue, issueAudit, outcome.Notifications, cancellationToken);
        return await Reload(issue.Id, 200, cancellationToken);
    }

    private static Task<Outcome> Done(IReadOnlyCollection<Notification>? notifications = null) =>
        Task.FromResult(new Outcome(null, notifications ?? []));

    private static Outcome InvalidState(EquipmentIssueReport issue, string message) =>
        new(Result<EquipmentIssueResponse>.Failure(
            $"{message} Current status: {issue.IssueStatus}.", "EQUIPMENT_ISSUE_INVALID_STATE", 409), []);

    /// <summary>Marks the issue done and records who confirmed the equipment safe to use (GBR-55).</summary>
    private void Close(EquipmentIssueReport issue, string note, string userId)
    {
        issue.IssueStatus = EquipmentIssueStatus.Resolved;
        issue.HandledByUserId = userId;
        issue.ResolvedAt = DateTime.UtcNow;
        issue.ResolutionNote = note.Trim();
        _repository.Add(new EquipmentSafetyConfirmation
        {
            EquipmentId = issue.EquipmentId,
            EquipmentIssueReportId = issue.Id,
            ConfirmedByUserId = userId,
            ConfirmedAt = DateTime.UtcNow,
            Note = note.Trim()
        });
    }

    /// <summary>Operational again, unless another open issue still keeps it out of use.</summary>
    private async Task BackInUseAsync(EquipmentIssueReport issue, CancellationToken cancellationToken)
    {
        if (issue.Equipment.EquipmentStatus is EquipmentStatus.Damaged or EquipmentStatus.UnderMaintenance
            && !await _repository.HasOpenIssueAsync(issue.EquipmentId, issue.Id, cancellationToken))
            issue.Equipment.EquipmentStatus = EquipmentStatus.Operational;
    }

    private async Task<IReadOnlyCollection<Notification>> NotifyRoleAsync(
        string role, string senderUserId, string title, string message, CancellationToken cancellationToken)
    {
        var recipients = await _repository.GetActiveUserIdsInRoleAsync(role, cancellationToken);
        return recipients
            .Where(id => id != senderUserId)
            .Select(id => AuditedWriter.Notify(id, senderUserId, title, message, IssueEntity))
            .ToArray();
    }

    private static IReadOnlyCollection<Notification> NotifyReporter(
        EquipmentIssueReport issue, string senderUserId, string title, string note) =>
        issue.ReportedByUserId == senderUserId
            ? []
            : [AuditedWriter.Notify(issue.ReportedByUserId, senderUserId, title,
                $"{issue.Equipment.EquipmentName}: {note.Trim()}", IssueEntity)];

    private async Task<Result<EquipmentIssueResponse>> Reload(int id, int statusCode, CancellationToken cancellationToken)
    {
        var issue = await _repository.GetIssueAsync(id, cancellationToken);
        return issue is null
            ? NotFound()
            : Result<EquipmentIssueResponse>.Success(_mapper.Map<EquipmentIssueResponse>(issue), statusCode);
    }

    private static Result<EquipmentIssueResponse> NotFound() =>
        Result<EquipmentIssueResponse>.Failure("Equipment issue was not found.", "EQUIPMENT_ISSUE_NOT_FOUND", 404);
}
