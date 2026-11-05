using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>UC-76 Manage Gym Equipment. GBR-55 (safe before Operational), GBR-56 (audit).</summary>
public sealed class EquipmentService : IEquipmentService
{
    private const string CategoryEntity = nameof(CatalogCategory);
    private const string EquipmentEntity = nameof(Equipment);

    private readonly IEquipmentRepository _repository;
    private readonly IMapper _mapper;

    public EquipmentService(IEquipmentRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    // ── Categories ───────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<EquipmentCategoryResponse>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _repository.GetCategoriesAsync(cancellationToken);
        var counts = await _repository.CountEquipmentByCategoryAsync(cancellationToken);
        IReadOnlyList<EquipmentCategoryResponse> items = categories
            .Select(c => ToResponse(c, counts.GetValueOrDefault(c.Id)))
            .ToArray();
        return Result<IReadOnlyList<EquipmentCategoryResponse>>.Success(items);
    }

    public async Task<Result<EquipmentCategoryResponse>> CreateCategoryAsync(
        EquipmentCategoryRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await _repository.CategoryNameExistsAsync(name, null, cancellationToken))
            return CategoryExists();

        var category = new CatalogCategory { CategoryName = name, Description = Clean(request.Description) };
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, CategoryEntity, ipAddress,
            newValue: $"Name={name}");
        await AuditedWriter.SaveNewAsync(_repository, category, audit, null, cancellationToken);
        return Result<EquipmentCategoryResponse>.Success(ToResponse(category, 0), 201);
    }

    public async Task<Result<EquipmentCategoryResponse>> UpdateCategoryAsync(
        int id, EquipmentCategoryRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var category = await _repository.GetCategoryAsync(id, cancellationToken);
        if (category is null)
            return CategoryNotFound<EquipmentCategoryResponse>();

        var name = request.Name.Trim();
        if (await _repository.CategoryNameExistsAsync(name, id, cancellationToken))
            return CategoryExists();

        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, CategoryEntity, ipAddress,
            $"Name={category.CategoryName}; Description={category.Description}",
            $"Name={name}; Description={Clean(request.Description)}");
        category.CategoryName = name;
        category.Description = Clean(request.Description);
        await AuditedWriter.SaveChangedAsync(_repository, category, audit, null, cancellationToken);

        var counts = await _repository.CountEquipmentByCategoryAsync(cancellationToken);
        return Result<EquipmentCategoryResponse>.Success(ToResponse(category, counts.GetValueOrDefault(id)));
    }

    public async Task<Result<bool>> DeleteCategoryAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var category = await _repository.GetCategoryAsync(id, cancellationToken);
        if (category is null)
            return CategoryNotFound<bool>();
        if (await _repository.CategoryInUseAsync(id, cancellationToken))
            return Result<bool>.Failure(
                "This category is used by equipment and cannot be removed.", "EQUIPMENT_CATEGORY_IN_USE", 409);

        // Soft delete (GBR-33): the global query filter hides it from now on.
        category.IsDeleted = true;
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Delete, CategoryEntity, ipAddress,
            oldValue: $"Name={category.CategoryName}");
        await AuditedWriter.SaveChangedAsync(_repository, category, audit, null, cancellationToken);
        return Result<bool>.Success(true);
    }

    // ── Equipment ────────────────────────────────────────────────

    public async Task<Result<PagedResponse<EquipmentResponse>>> GetEquipmentListAsync(
        EquipmentListRequest request, CancellationToken cancellationToken = default)
    {
        EquipmentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParse(request.Status, out EquipmentStatus parsed))
                return Result<PagedResponse<EquipmentResponse>>.Failure("Status filter is invalid.", "EQUIPMENT_INVALID_STATUS");
            status = parsed;
        }

        var (items, total) = await _repository.SearchEquipmentAsync(
            Clean(request.Search), request.CategoryId, status, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<EquipmentResponse>>.Success(new PagedResponse<EquipmentResponse>(
            request.Page, request.PageSize, total, items.Select(_mapper.Map<EquipmentResponse>).ToArray()));
    }

    public async Task<Result<EquipmentResponse>> GetEquipmentAsync(int id, CancellationToken cancellationToken = default)
    {
        var equipment = await _repository.GetEquipmentAsync(id, cancellationToken);
        return equipment is null ? EquipmentNotFound() : Result<EquipmentResponse>.Success(_mapper.Map<EquipmentResponse>(equipment));
    }

    public async Task<Result<EquipmentResponse>> CreateEquipmentAsync(
        EquipmentRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var check = await ValidateReferencesAsync(request, null, cancellationToken);
        if (check is not null)
            return check;

        var gymId = await _repository.GetDefaultGymIdAsync(cancellationToken);
        if (gymId is null)
            return Result<EquipmentResponse>.Failure("No gym configured in the system.", "EQUIPMENT_NO_GYM", 500);

        var equipment = new Equipment { GymId = gymId.Value, EquipmentStatus = EquipmentStatus.Operational };
        Apply(equipment, request);
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, EquipmentEntity, ipAddress,
            newValue: $"Name={equipment.EquipmentName}; Code={equipment.EquipmentCode}; Status={equipment.EquipmentStatus}");
        await AuditedWriter.SaveNewAsync(_repository, equipment, audit, null, cancellationToken);

        var saved = await _repository.GetEquipmentAsync(equipment.Id, cancellationToken) ?? equipment;
        return Result<EquipmentResponse>.Success(_mapper.Map<EquipmentResponse>(saved), 201);
    }

    public async Task<Result<EquipmentResponse>> UpdateEquipmentAsync(
        int id, EquipmentRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var equipment = await _repository.GetEquipmentAsync(id, cancellationToken);
        if (equipment is null)
            return EquipmentNotFound();

        var check = await ValidateReferencesAsync(request, id, cancellationToken);
        if (check is not null)
            return check;

        var before = Describe(equipment);
        Apply(equipment, request);
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, EquipmentEntity, ipAddress, before, Describe(equipment));
        await AuditedWriter.SaveChangedAsync(_repository, equipment, audit, null, cancellationToken);

        var saved = await _repository.GetEquipmentAsync(id, cancellationToken) ?? equipment;
        return Result<EquipmentResponse>.Success(_mapper.Map<EquipmentResponse>(saved));
    }

    public async Task<Result<EquipmentResponse>> ChangeStatusAsync(
        int id, EquipmentStatusRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var equipment = await _repository.GetEquipmentAsync(id, cancellationToken);
        if (equipment is null)
            return EquipmentNotFound();
        if (!TryParse(request.Status, out EquipmentStatus status))
            return Result<EquipmentResponse>.Failure("Status is invalid.", "EQUIPMENT_INVALID_STATUS");
        if (equipment.EquipmentStatus == status)
            return Result<EquipmentResponse>.Failure(
                $"Equipment is already {status}.", "EQUIPMENT_STATUS_UNCHANGED", 409);
        if (equipment.EquipmentStatus == EquipmentStatus.Retired)
            return Result<EquipmentResponse>.Failure(
                "Retired equipment cannot change status.", "EQUIPMENT_RETIRED", 409);
        if (status == EquipmentStatus.Retired && string.IsNullOrWhiteSpace(request.Note))
            return Result<EquipmentResponse>.Failure("A reason is required to retire equipment.", "EQUIPMENT_NOTE_REQUIRED");
        // GBR-55: back to Operational only through a safety confirmation on its open issue.
        if (status == EquipmentStatus.Operational && await _repository.HasOpenIssueAsync(id, null, cancellationToken))
            return Result<EquipmentResponse>.Failure(
                "This equipment has an open issue. Resolve it in Handle Equipment Issues or Manage Equipment Repairs.",
                "EQUIPMENT_HAS_OPEN_ISSUE", 409);

        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, EquipmentEntity, ipAddress,
            $"Status={equipment.EquipmentStatus}", $"Status={status}; Note={Clean(request.Note)}");
        equipment.EquipmentStatus = status;
        if (!string.IsNullOrWhiteSpace(request.Note))
            equipment.Note = request.Note.Trim();
        await AuditedWriter.SaveChangedAsync(_repository, equipment, audit, null, cancellationToken);
        return Result<EquipmentResponse>.Success(_mapper.Map<EquipmentResponse>(equipment));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<Result<EquipmentResponse>?> ValidateReferencesAsync(
        EquipmentRequest request, int? equipmentId, CancellationToken cancellationToken)
    {
        if (request.CategoryId is { } categoryId && await _repository.GetCategoryAsync(categoryId, cancellationToken) is null)
            return Result<EquipmentResponse>.Failure("Equipment category was not found.", "EQUIPMENT_CATEGORY_NOT_FOUND");
        var code = Clean(request.Code);
        if (code is not null && await _repository.EquipmentCodeExistsAsync(code, equipmentId, cancellationToken))
            return Result<EquipmentResponse>.Failure("Equipment with this code already exists.", "EQUIPMENT_CODE_EXISTS", 409);
        return null;
    }

    private static void Apply(Equipment equipment, EquipmentRequest request)
    {
        equipment.EquipmentName = request.Name.Trim();
        equipment.CatalogCategoryId = request.CategoryId;
        equipment.EquipmentCode = Clean(request.Code);
        equipment.EquipmentBrand = Clean(request.Brand);
        equipment.EquipmentModel = Clean(request.Model);
        equipment.PurchaseDate = request.PurchaseDate;
        equipment.PurchasePrice = request.PurchasePrice;
        equipment.EquipmentDescriptionUrl = Clean(request.DescriptionUrl);
        equipment.Note = Clean(request.Note);
    }

    private static string Describe(Equipment e) =>
        $"Name={e.EquipmentName}; Code={e.EquipmentCode}; CategoryId={e.CatalogCategoryId}; Brand={e.EquipmentBrand}; " +
        $"Model={e.EquipmentModel}; PurchaseDate={e.PurchaseDate}; PurchasePrice={e.PurchasePrice}";

    internal static bool TryParse<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed) &&
        !int.TryParse(value, out _);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EquipmentCategoryResponse ToResponse(CatalogCategory c, int count) =>
        new(c.Id, c.CategoryName, c.Description, count);

    private static Result<EquipmentCategoryResponse> CategoryExists() =>
        Result<EquipmentCategoryResponse>.Failure("A category with this name already exists.", "EQUIPMENT_CATEGORY_EXISTS", 409);

    private static Result<T> CategoryNotFound<T>() =>
        Result<T>.Failure("Equipment category was not found.", "EQUIPMENT_CATEGORY_NOT_FOUND", 404);

    private static Result<EquipmentResponse> EquipmentNotFound() =>
        Result<EquipmentResponse>.Failure("Equipment was not found.", "EQUIPMENT_NOT_FOUND", 404);
}
