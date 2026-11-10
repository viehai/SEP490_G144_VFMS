using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// PT package freeze request.
/// Manager reviews and processes PT freeze requests (UC-34, GBR-15).
/// Approving cancels scheduled sessions inside the freeze window (GBR-17).
/// </summary>
public class PTFreeze : BaseEntity
{
    public int PTPurchaseId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateOnly RequestedStartDate { get; set; }
    public DateOnly RequestedEndDate { get; set; }
    public DateOnly? ActualStartDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public PTFreezeRequestStatus RequestStatus { get; set; } = PTFreezeRequestStatus.Pending;
    public string? ReviewNote { get; set; }
    public string? Reason { get; set; }
    public string? SupportingDocumentUrl { get; set; }
    public int? MinimumDaysSnapshot { get; set; }
    public int? MaxDaysSnapshot { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PTPurchase PTPurchase { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

