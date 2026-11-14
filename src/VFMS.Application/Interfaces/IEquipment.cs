using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IEquipmentRepository : IAuditedRepository
{
    // Categories
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, int>> CountEquipmentByCategoryAsync(CancellationToken cancellationToken = default);
    Task<CatalogCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> CategoryNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default);
    Task<bool> CategoryInUseAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> CategoryPrefixExistsAsync(string prefix, int? excludingId, CancellationToken cancellationToken = default);

    // Equipment
    Task<(IReadOnlyList<Equipment> Items, int TotalCount)> SearchEquipmentAsync(
        string? search, int? categoryId, EquipmentStatus? status, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<Equipment?> GetEquipmentAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> EquipmentCodeExistsAsync(string code, int? excludingId, CancellationToken cancellationToken = default);
    /// <summary>Highest number used in codes "PREFIX-NNNN" (deleted equipment included, so numbers are never reused); 0 when none.</summary>
    Task<int> GetMaxCodeNumberAsync(string prefix, CancellationToken cancellationToken = default);
    Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default);

    // Issues
    Task<bool> HasOpenIssueAsync(int equipmentId, int? excludingIssueId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<EquipmentIssueReport> Items, int TotalCount)> SearchIssuesAsync(
        EquipmentIssueStatus? status, int? equipmentId, string? reportedByUserId, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<EquipmentIssueReport?> GetIssueAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
}

/// <summary>UC-76 Manage Gym Equipment (categories and equipment records).</summary>
public interface IEquipmentService
{
    Task<Result<IReadOnlyList<EquipmentCategoryResponse>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Result<EquipmentCategoryResponse>> CreateCategoryAsync(
        EquipmentCategoryRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentCategoryResponse>> UpdateCategoryAsync(
        int id, EquipmentCategoryRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteCategoryAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<EquipmentResponse>>> GetEquipmentListAsync(
        EquipmentListRequest request, CancellationToken cancellationToken = default);
    Task<Result<EquipmentResponse>> GetEquipmentAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<EquipmentResponse>> CreateEquipmentAsync(
        EquipmentRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentResponse>> UpdateEquipmentAsync(
        int id, EquipmentRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentResponse>> ChangeStatusAsync(
        int id, EquipmentStatusRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>UC-71 Report, UC-72 Handle (Staff) and UC-73 Repair (Manager) equipment issues.</summary>
public interface IEquipmentIssueService
{
    Task<Result<EquipmentIssueResponse>> ReportAsync(
        ReportEquipmentIssueRequest request, string reporterUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<EquipmentIssueResponse>>> GetIssuesAsync(
        EquipmentIssueListRequest request, string? onlyReportedByUserId, CancellationToken cancellationToken = default);
    /// <summary>Returns the issue; when <paramref name="onlyReportedByUserId"/> is set, other users' issues are not found.</summary>
    Task<Result<EquipmentIssueResponse>> GetIssueAsync(
        int id, string? onlyReportedByUserId, CancellationToken cancellationToken = default);

    // Staff (UC-72)
    Task<Result<EquipmentIssueResponse>> StartHandlingAsync(int id, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> ResolveAsync(int id, string note, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> EscalateAsync(int id, string note, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> MarkSafeAsync(int id, string note, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // Manager (UC-73)
    Task<Result<EquipmentIssueResponse>> StartRepairAsync(int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> ConfirmSafeAsync(int id, string note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> MarkNotSafeAsync(int id, string note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<EquipmentIssueResponse>> DisposeAsync(int id, string note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
