using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// A Yoga Instructor asks the Manager to open a class; the Manager approves (by saving the create-class form filled
/// from the request) or rejects with a reason.
/// GBR-128 submit: own request only, start at least 7 days away, weekday count = sessions/week, preferred studio active
/// and big enough, at most 3 pending per instructor.
/// GBR-129 only a Pending request can be approved, rejected or withdrawn; a rejection needs a reason.
/// GBR-130 approving creates a Draft class through the normal create-class rules; publishing still follows GBR-77.
/// </summary>
public sealed class YogaClassOpeningService : IYogaClassOpeningService
{
    public const int MaxPendingPerInstructor = 3;   // GBR-128
    private const string Entity = nameof(YogaClassOpeningRequest);
    private const string ManagerRole = "Manager";

    private readonly IYogaClassOpeningRepository _requests;
    private readonly IYogaRepository _yoga;
    private readonly IYogaClassService _classes;
    private readonly TimeProvider _clock;

    public YogaClassOpeningService(
        IYogaClassOpeningRepository requests, IYogaRepository yoga, IYogaClassService classes, TimeProvider clock)
    {
        _requests = requests;
        _yoga = yoga;
        _classes = classes;
        _clock = clock;
    }

    // ── Instructor ───────────────────────────────────────────────

    public async Task<Result<YogaClassOpeningOptionsResponse>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var types = await _yoga.GetClassTypesAsync(cancellationToken);
        var studios = await _yoga.GetStudiosAsync(false, cancellationToken);
        return Result<YogaClassOpeningOptionsResponse>.Success(new(
            types.Select(t => new YogaClassTypeResponse(t.Id, t.TypeName, t.Description)).ToArray(),
            studios.Select(s => new YogaStudioResponse(s.Id, s.StudioName, s.FloorNumber, s.Capacity, s.IsActive)).ToArray()));
    }

    public async Task<Result<IReadOnlyList<YogaClassOpeningRequestResponse>>> GetMineAsync(
        string instructorUserId, CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<YogaClassOpeningRequestResponse>>.Success(
            await ToResponsesAsync(await _requests.ListAsync(instructorUserId, null, cancellationToken), cancellationToken));

    public async Task<Result<YogaClassOpeningRequestResponse>> SubmitAsync(
        string instructorUserId, YogaClassOpeningRequestRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var earliest = YogaTime.Today(_clock).AddDays(YogaClassService.PublishLeadDays);
        if (request.StartDate < earliest)
            return Fail($"Ngày bắt đầu phải từ {earliest:dd/MM/yyyy} trở đi (ít nhất {YogaClassService.PublishLeadDays} ngày nữa) để kịp công bố lớp.",
                "YOGA_REQUEST_START_TOO_SOON");
        if (await _yoga.GetClassTypeAsync(request.YogaClassTypeId, cancellationToken) is null)
            return Fail("Không tìm thấy loại lớp.", "YOGA_TYPE_NOT_FOUND");
        if (request.PreferredStudioId is { } studioId)
        {
            var studio = await _yoga.GetStudioAsync(studioId, cancellationToken);
            if (studio is null || !studio.IsActive)
                return Fail("Phòng yoga không khả dụng.", "YOGA_STUDIO_UNAVAILABLE");
            if (studio.Capacity < request.MaxParticipants)
                return Fail($"Phòng {studio.StudioName} có {studio.Capacity} thảm nhưng lớp muốn tối đa {request.MaxParticipants} học viên.",
                    "YOGA_STUDIO_TOO_SMALL");
        }
        if (await _requests.CountPendingAsync(instructorUserId, cancellationToken) >= MaxPendingPerInstructor)
            return Fail($"Bạn đang có {MaxPendingPerInstructor} yêu cầu chờ duyệt. Hãy chờ quản lý xử lý hoặc rút bớt một yêu cầu.",
                "YOGA_REQUEST_PENDING_LIMIT", 409);

        var entity = new YogaClassOpeningRequest
        {
            InstructorUserId = instructorUserId,
            YogaClassTypeId = request.YogaClassTypeId,
            ClassName = request.ClassName.Trim(),
            Description = Clean(request.Description),
            DifficultyLevel = Contracts.Yoga.Validators.YogaRules.DifficultyLevels
                .First(d => d.Equals(request.DifficultyLevel.Trim(), StringComparison.OrdinalIgnoreCase)),
            SessionsPerWeek = request.SessionsPerWeek,
            MinParticipants = request.MinParticipants,
            MaxParticipants = request.MaxParticipants,
            DurationMinutes = request.DurationMinutes,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PreferredDays = string.Join(',', request.PreferredDays.Distinct().OrderBy(d => ((int)d + 6) % 7)),
            PreferredStartTime = request.PreferredStartTime,
            PreferredStudioId = request.PreferredStudioId,
            Note = Clean(request.Note),
            RequestStatus = YogaClassRequestStatus.Pending
        };
        var names = await _yoga.GetUserNamesAsync([instructorUserId], cancellationToken);
        var instructorName = names.GetValueOrDefault(instructorUserId, instructorUserId);
        var notices = (await _requests.GetActiveUserIdsInRoleAsync(ManagerRole, cancellationToken))
            .Select(managerId => AuditedWriter.Notify(managerId, instructorUserId, "Yêu cầu mở lớp yoga mới",
                $"{instructorName} đề xuất mở lớp {entity.ClassName} từ {entity.StartDate:dd/MM/yyyy}.", Entity, NotificationType.YogaClassRequest))
            .ToArray();
        var audit = AuditedWriter.Audit(instructorUserId, AuditActionType.Create, Entity, ipAddress, newValue: Describe(entity));
        await AuditedWriter.SaveNewAsync(_requests, entity, audit, notices, cancellationToken);
        return await ResponseAsync(entity.Id, 201, cancellationToken);   // MSG-54
    }

    public async Task<Result<YogaClassOpeningRequestResponse>> WithdrawAsync(
        string instructorUserId, int id, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Result<YogaClassOpeningRequestResponse>? failure = null;
        await _requests.ExecuteInTransactionAsync(async () =>
        {
            await _requests.LockAsync(id, cancellationToken);
            var entity = await _requests.GetAsync(id, cancellationToken);
            if (entity is null || entity.InstructorUserId != instructorUserId)
            {
                failure = NotFound();
                return;
            }
            if (entity.RequestStatus != YogaClassRequestStatus.Pending)
            {
                failure = AlreadyDecided<YogaClassOpeningRequestResponse>();
                return;
            }
            var before = Describe(entity);
            entity.RequestStatus = YogaClassRequestStatus.Withdrawn;
            entity.UpdatedAt = DateTime.UtcNow;
            var audit = AuditedWriter.Audit(instructorUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(entity));
            await AuditedWriter.SaveChangedAsync(_requests, entity, audit, null, cancellationToken);
        }, cancellationToken);
        return failure ?? await ResponseAsync(id, 200, cancellationToken);
    }

    // ── Manager ──────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<YogaClassOpeningRequestResponse>>> GetAllAsync(
        string? status, CancellationToken cancellationToken = default)
    {
        YogaClassRequestStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<YogaClassRequestStatus>(status.Trim(), true, out var parsed))
                return Result<IReadOnlyList<YogaClassOpeningRequestResponse>>.Failure("Trạng thái không hợp lệ.", "YOGA_REQUEST_INVALID_STATUS");
            filter = parsed;
        }
        return Result<IReadOnlyList<YogaClassOpeningRequestResponse>>.Success(
            await ToResponsesAsync(await _requests.ListAsync(null, filter, cancellationToken), cancellationToken));
    }

    public async Task<Result<YogaClassResponse>> ApproveAsync(
        int id, YogaClassRequest classForm, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Result<YogaClassResponse>? result = null;
        await _requests.ExecuteInTransactionAsync(async () =>
        {
            await _requests.LockAsync(id, cancellationToken);
            var entity = await _requests.GetAsync(id, cancellationToken);
            if (entity is null)
            {
                result = Result<YogaClassResponse>.Failure(NotFoundMessage, "YOGA_REQUEST_NOT_FOUND", 404);
                return;
            }
            if (entity.RequestStatus != YogaClassRequestStatus.Pending)
            {
                result = AlreadyDecided<YogaClassResponse>();
                return;
            }

            // GBR-130: the same rules as creating a class by hand.
            var created = await _classes.CreateClassAsync(classForm, managerUserId, ipAddress, cancellationToken);
            if (!created.IsSuccess)
            {
                result = created;
                return;
            }

            var before = Describe(entity);
            entity.RequestStatus = YogaClassRequestStatus.Approved;
            entity.ReviewedByUserId = managerUserId;
            entity.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
            entity.YogaClassId = created.Data!.Id;
            entity.UpdatedAt = DateTime.UtcNow;
            var notice = AuditedWriter.Notify(entity.InstructorUserId, managerUserId, "Yêu cầu mở lớp đã được duyệt",
                $"Lớp {created.Data.ClassName} đã được tạo (bản nháp). Quản lý sẽ xếp lịch và công bố lớp.", Entity,
                NotificationType.YogaClassRequest);
            var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(entity));
            await AuditedWriter.SaveChangedAsync(_requests, entity, audit, [notice], cancellationToken);
            result = created;   // MSG-55
        }, cancellationToken);
        return result!;
    }

    public async Task<Result<YogaClassOpeningRequestResponse>> RejectAsync(
        int id, string reason, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Fail("Vui lòng nhập lý do từ chối.", "YOGA_REQUEST_REASON_REQUIRED");   // GBR-129
        Result<YogaClassOpeningRequestResponse>? failure = null;
        await _requests.ExecuteInTransactionAsync(async () =>
        {
            await _requests.LockAsync(id, cancellationToken);
            var entity = await _requests.GetAsync(id, cancellationToken);
            if (entity is null)
            {
                failure = NotFound();
                return;
            }
            if (entity.RequestStatus != YogaClassRequestStatus.Pending)
            {
                failure = AlreadyDecided<YogaClassOpeningRequestResponse>();
                return;
            }
            var before = Describe(entity);
            entity.RequestStatus = YogaClassRequestStatus.Rejected;
            entity.RejectReason = reason.Trim();
            entity.ReviewedByUserId = managerUserId;
            entity.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
            entity.UpdatedAt = DateTime.UtcNow;
            var notice = AuditedWriter.Notify(entity.InstructorUserId, managerUserId, "Yêu cầu mở lớp bị từ chối",
                $"Yêu cầu mở lớp {entity.ClassName} bị từ chối. Lý do: {entity.RejectReason}", Entity, NotificationType.YogaClassRequest);
            var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(entity));
            await AuditedWriter.SaveChangedAsync(_requests, entity, audit, [notice], cancellationToken);
        }, cancellationToken);
        return failure ?? await ResponseAsync(id, 200, cancellationToken);   // MSG-56
    }

    // ── Mapping ──────────────────────────────────────────────────

    private async Task<Result<YogaClassOpeningRequestResponse>> ResponseAsync(int id, int statusCode, CancellationToken cancellationToken)
    {
        var entity = await _requests.GetAsync(id, cancellationToken);
        return entity is null
            ? NotFound()
            : Result<YogaClassOpeningRequestResponse>.Success((await ToResponsesAsync([entity], cancellationToken))[0], statusCode);
    }

    private async Task<IReadOnlyList<YogaClassOpeningRequestResponse>> ToResponsesAsync(
        IReadOnlyList<YogaClassOpeningRequest> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(r => r.InstructorUserId).Concat(items.Select(r => r.ReviewedByUserId).OfType<string>());
        var names = await _yoga.GetUserNamesAsync(ids, cancellationToken);
        return items.Select(r => new YogaClassOpeningRequestResponse(
            r.Id, r.InstructorUserId, r.Instructor?.FullName ?? names.GetValueOrDefault(r.InstructorUserId, string.Empty),
            r.YogaClassTypeId, r.YogaClassType?.TypeName ?? string.Empty, r.ClassName, r.Description, r.DifficultyLevel,
            r.SessionsPerWeek, r.MinParticipants, r.MaxParticipants, r.DurationMinutes, r.StartDate, r.EndDate,
            ParseDays(r.PreferredDays), r.PreferredStartTime, r.PreferredStudioId, r.PreferredStudio?.StudioName, r.Note,
            r.RequestStatus.ToString(), r.RejectReason,
            r.ReviewedByUserId is null ? null : names.GetValueOrDefault(r.ReviewedByUserId),
            r.ReviewedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            r.YogaClassId, DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))).ToArray();
    }

    public static DayOfWeek[] ParseDays(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.TryParse<DayOfWeek>(d, out var day) ? (DayOfWeek?)day : null)
            .OfType<DayOfWeek>()
            .ToArray();

    private static string Describe(YogaClassOpeningRequest r) =>
        $"Name={r.ClassName}; Type={r.YogaClassTypeId}; Instructor={r.InstructorUserId}; Level={r.DifficultyLevel}; " +
        $"Participants={r.MinParticipants}-{r.MaxParticipants}; Duration={r.DurationMinutes}; PerWeek={r.SessionsPerWeek}; " +
        $"Dates={r.StartDate:yyyy-MM-dd}..{r.EndDate:yyyy-MM-dd}; Days={r.PreferredDays}@{r.PreferredStartTime:HH\\:mm}; " +
        $"Studio={r.PreferredStudioId?.ToString() ?? "-"}; Status={r.RequestStatus}; Class={r.YogaClassId?.ToString() ?? "-"}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string NotFoundMessage = "Không tìm thấy yêu cầu mở lớp.";

    private static Result<YogaClassOpeningRequestResponse> Fail(string message, string code, int status = 400) =>
        Result<YogaClassOpeningRequestResponse>.Failure(message, code, status);

    private static Result<YogaClassOpeningRequestResponse> NotFound() => Fail(NotFoundMessage, "YOGA_REQUEST_NOT_FOUND", 404);

    /// <summary>MSG-57.</summary>
    private static Result<T> AlreadyDecided<T>() =>
        Result<T>.Failure("Yêu cầu này đã được xử lý. Hãy tải lại trang.", "YOGA_REQUEST_ALREADY_DECIDED", 409);
}
