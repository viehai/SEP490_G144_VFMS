using System.Text.RegularExpressions;
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
        var items = new List<EquipmentCategoryResponse>(categories.Count);
        foreach (var c in categories)
            items.Add(ToResponse(c, counts.GetValueOrDefault(c.Id), await NextCodeAsync(c.CodePrefix, cancellationToken)));
        return Result<IReadOnlyList<EquipmentCategoryResponse>>.Success(items);
    }

    public async Task<Result<EquipmentCategoryResponse>> CreateCategoryAsync(
        EquipmentCategoryRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await _repository.CategoryNameExistsAsync(name, null, cancellationToken))
            return CategoryExists();
        var prefix = NormalizePrefix(request.CodePrefix);
        if (await _repository.CategoryPrefixExistsAsync(prefix, null, cancellationToken))
            return PrefixExists();

        var category = new CatalogCategory { CategoryName = name, Description = Clean(request.Description), CodePrefix = prefix };
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, CategoryEntity, ipAddress,
            newValue: $"Name={name}; CodePrefix={prefix}");
        await AuditedWriter.SaveNewAsync(_repository, category, audit, null, cancellationToken);
        return Result<EquipmentCategoryResponse>.Success(
            ToResponse(category, 0, await NextCodeAsync(prefix, cancellationToken)), 201);
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
        var prefix = NormalizePrefix(request.CodePrefix);
        if (await _repository.CategoryPrefixExistsAsync(prefix, id, cancellationToken))
            return PrefixExists();

        // Changing the prefix only affects codes generated from now on; existing codes are kept.
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, CategoryEntity, ipAddress,
            $"Name={category.CategoryName}; Description={category.Description}; CodePrefix={category.CodePrefix}",
            $"Name={name}; Description={Clean(request.Description)}; CodePrefix={prefix}");
        category.CategoryName = name;
        category.Description = Clean(request.Description);
        category.CodePrefix = prefix;
        await AuditedWriter.SaveChangedAsync(_repository, category, audit, null, cancellationToken);

        var counts = await _repository.CountEquipmentByCategoryAsync(cancellationToken);
        return Result<EquipmentCategoryResponse>.Success(
            ToResponse(category, counts.GetValueOrDefault(id), await NextCodeAsync(prefix, cancellationToken)));
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
        var (error, code) = await ResolveCodeAsync(request, null, cancellationToken);
        if (error is not null)
            return error;

        var gymId = await _repository.GetDefaultGymIdAsync(cancellationToken);
        if (gymId is null)
            return Result<EquipmentResponse>.Failure("No gym configured in the system.", "EQUIPMENT_NO_GYM", 500);

        var equipment = new Equipment { GymId = gymId.Value, EquipmentStatus = EquipmentStatus.Operational };
        Apply(equipment, request, code);
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

        var (error, code) = await ResolveCodeAsync(request, equipment, cancellationToken);
        if (error is not null)
            return error;

        var before = Describe(equipment);
        Apply(equipment, request, code);
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

    /// <summary>
    /// Works out the equipment code (UC-76). A blank code gets the next PREFIX-NNNN of the category; a typed code must
    /// follow that format and be unused. A record whose category and code do not change keeps its code, so equipment
    /// created before code prefixes existed can still be edited.
    /// </summary>
    private async Task<(Result<EquipmentResponse>? Error, string? Code)> ResolveCodeAsync(
        EquipmentRequest request, Equipment? existing, CancellationToken cancellationToken)
    {
        var category = request.CategoryId is { } categoryId ? await _repository.GetCategoryAsync(categoryId, cancellationToken) : null;
        if (category is null)
            return (Result<EquipmentResponse>.Failure("Equipment category was not found.", "EQUIPMENT_CATEGORY_NOT_FOUND"), null);

        var typed = Clean(request.Code)?.ToUpperInvariant();
        if (existing is not null && existing.CatalogCategoryId == category.Id && typed is not null &&
            string.Equals(typed, existing.EquipmentCode, StringComparison.OrdinalIgnoreCase))
            return (null, existing.EquipmentCode);

        if (category.CodePrefix is not { } prefix)
            return (Result<EquipmentResponse>.Failure(
                "This category has no code prefix. Set a code prefix on the category first.", "EQUIPMENT_CATEGORY_PREFIX_MISSING"), null);

        if (typed is null)
            return (null, FormatCode(prefix, await _repository.GetMaxCodeNumberAsync(prefix, cancellationToken) + 1));

        if (!Regex.IsMatch(typed, "^" + Regex.Escape(prefix) + @"-\d{4}$"))
            return (Result<EquipmentResponse>.Failure(
                $"Equipment code must look like {prefix}-0001 for this category.", "EQUIPMENT_CODE_INVALID_FORMAT"), null);
        if (await _repository.EquipmentCodeExistsAsync(typed, existing?.Id, cancellationToken))
            return (Result<EquipmentResponse>.Failure("Equipment with this code already exists.", "EQUIPMENT_CODE_EXISTS", 409), null);
        return (null, typed);
    }

    private async Task<string?> NextCodeAsync(string? prefix, CancellationToken cancellationToken) =>
        prefix is null ? null : FormatCode(prefix, await _repository.GetMaxCodeNumberAsync(prefix, cancellationToken) + 1);

    internal static string FormatCode(string prefix, int number) => $"{prefix}-{number:D4}";

    private static string NormalizePrefix(string? prefix) => (prefix ?? string.Empty).Trim().ToUpperInvariant();

    private static void Apply(Equipment equipment, EquipmentRequest request, string? code)
    {
        equipment.EquipmentName = request.Name.Trim();
        equipment.CatalogCategoryId = request.CategoryId;
        equipment.EquipmentCode = code;
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

    private static EquipmentCategoryResponse ToResponse(CatalogCategory c, int count, string? nextCode) =>
        new(c.Id, c.CategoryName, c.Description, count, c.CodePrefix, nextCode);

    private static Result<EquipmentCategoryResponse> PrefixExists() =>
        Result<EquipmentCategoryResponse>.Failure(
            "Another category already uses this code prefix.", "EQUIPMENT_CATEGORY_PREFIX_EXISTS", 409);

    private static Result<EquipmentCategoryResponse> CategoryExists() =>
        Result<EquipmentCategoryResponse>.Failure("A category with this name already exists.", "EQUIPMENT_CATEGORY_EXISTS", 409);

    private static Result<T> CategoryNotFound<T>() =>
        Result<T>.Failure("Equipment category was not found.", "EQUIPMENT_CATEGORY_NOT_FOUND", 404);

    private static Result<EquipmentResponse> EquipmentNotFound() =>
        Result<EquipmentResponse>.Failure("Equipment was not found.", "EQUIPMENT_NOT_FOUND", 404);
}
