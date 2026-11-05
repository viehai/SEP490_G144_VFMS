namespace VFMS.Contracts.Equipment;

// ── Categories (UC-76) ─────────────────────────────────────────

public sealed record EquipmentCategoryRequest(string Name, string? Description = null);

public sealed record EquipmentCategoryResponse(int Id, string Name, string? Description, int EquipmentCount);

// ── Equipment (UC-76) ──────────────────────────────────────────

/// <summary>Create or update an equipment record. New equipment starts as "Operational".</summary>
public sealed record EquipmentRequest(
    string Name,
    int? CategoryId = null,
    string? Code = null,
    string? Brand = null,
    string? Model = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchasePrice = null,
    string? DescriptionUrl = null,
    string? Note = null
);

/// <summary>Status: Operational, UnderMaintenance, Damaged, Retired. A note is required to retire.</summary>
public sealed record EquipmentStatusRequest(string Status, string? Note = null);

public sealed record EquipmentListRequest(
    string? Search = null,
    int? CategoryId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record EquipmentResponse(
    int Id,
    string Name,
    string? Code,
    int? CategoryId,
    string? CategoryName,
    string? Brand,
    string? Model,
    string Status,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice,
    string? DescriptionUrl,
    string? Note,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── Issues (UC-71, UC-72, UC-73) ───────────────────────────────

/// <summary>
/// Incident type: Malfunction, Damage, Safety, Other. Set <see cref="ConfirmDuplicate"/> to report even
/// when the equipment already has an open issue.
/// </summary>
public sealed record ReportEquipmentIssueRequest(
    int EquipmentId,
    string IncidentType,
    string Description,
    string? ImageUrl = null,
    bool ConfirmDuplicate = false
);

/// <summary>Status: Reported, InProgress, Escalated, Resolved, MarkedSafe.</summary>
public sealed record EquipmentIssueListRequest(
    string? Status = null,
    int? EquipmentId = null,
    int Page = 1,
    int PageSize = 20
);

/// <summary>Resolution note, escalation reason, or repair note, depending on the action.</summary>
public sealed record EquipmentIssueActionRequest(string Note);

public sealed record SafetyConfirmationResponse(
    int Id,
    string ConfirmedByUserId,
    string? ConfirmedByName,
    DateTime ConfirmedAt,
    string? Note
);

public sealed record EquipmentIssueResponse(
    int Id,
    int EquipmentId,
    string EquipmentName,
    string? EquipmentCode,
    string EquipmentStatus,
    string IncidentType,
    string Description,
    string? ImageUrl,
    string Status,
    DateTime ReportedAt,
    string ReportedByUserId,
    string? ReportedByName,
    string? HandledByUserId,
    string? HandledByName,
    DateTime? ResolvedAt,
    string? ResolutionNote,
    IReadOnlyCollection<SafetyConfirmationResponse> SafetyConfirmations
);
