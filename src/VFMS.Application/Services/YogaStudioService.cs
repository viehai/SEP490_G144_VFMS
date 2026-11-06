using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>UC-34 Manage Yoga Studios. GBR-71 (unique name per gym, floor number), GBR-72 (deactivate only without future sessions).</summary>
public sealed class YogaStudioService : IYogaStudioService
{
    private const string Entity = nameof(YogaStudio);
    private readonly IYogaRepository _repository;
    private readonly TimeProvider _clock;

    public YogaStudioService(IYogaRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<YogaStudioResponse>>> GetStudiosAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var studios = await _repository.GetStudiosAsync(includeInactive, cancellationToken);
        return Result<IReadOnlyList<YogaStudioResponse>>.Success(studios.Select(ToResponse).ToArray());
    }

    public async Task<Result<YogaStudioResponse>> CreateAsync(
        YogaStudioRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var gymId = await _repository.GetDefaultGymIdAsync(cancellationToken);
        if (gymId is null)
            return Result<YogaStudioResponse>.Failure("No gym configured in the system.", "YOGA_NO_GYM", 500);

        var name = request.StudioName.Trim();
        if (await _repository.StudioNameExistsAsync(gymId.Value, name, null, cancellationToken))
            return NameTaken();

        var studio = new YogaStudio
        {
            GymId = gymId.Value, StudioName = name, FloorNumber = request.FloorNumber,
            Capacity = request.Capacity, IsActive = true
        };
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, Entity, ipAddress, newValue: Describe(studio));
        await AuditedWriter.SaveNewAsync(_repository, studio, audit, null, cancellationToken);
        return Result<YogaStudioResponse>.Success(ToResponse(studio), 201);
    }

    public async Task<Result<YogaStudioResponse>> UpdateAsync(
        int id, YogaStudioRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var studio = await _repository.GetStudioAsync(id, cancellationToken);
        if (studio is null)
            return NotFound();

        var name = request.StudioName.Trim();
        if (await _repository.StudioNameExistsAsync(studio.GymId, name, id, cancellationToken))
            return NameTaken();

        var before = Describe(studio);
        studio.StudioName = name;
        studio.FloorNumber = request.FloorNumber;
        studio.Capacity = request.Capacity;
        studio.UpdatedAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(studio));
        await AuditedWriter.SaveChangedAsync(_repository, studio, audit, null, cancellationToken);
        return Result<YogaStudioResponse>.Success(ToResponse(studio));
    }

    public async Task<Result<YogaStudioResponse>> SetActiveAsync(
        int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var studio = await _repository.GetStudioAsync(id, cancellationToken);
        if (studio is null)
            return NotFound();
        if (studio.IsActive == isActive)
            return Result<YogaStudioResponse>.Success(ToResponse(studio));

        if (!isActive && await _repository.StudioHasFutureSessionsAsync(id, YogaTime.Today(_clock), cancellationToken))
            return Result<YogaStudioResponse>.Failure(
                "This studio has upcoming class sessions. Move or cancel them before deactivating the studio.",
                "YOGA_STUDIO_HAS_SESSIONS", 409);   // GBR-72

        var before = Describe(studio);
        studio.IsActive = isActive;
        studio.UpdatedAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, Entity, ipAddress, before, Describe(studio));
        await AuditedWriter.SaveChangedAsync(_repository, studio, audit, null, cancellationToken);
        return Result<YogaStudioResponse>.Success(ToResponse(studio));
    }

    private static YogaStudioResponse ToResponse(YogaStudio s) => new(s.Id, s.StudioName, s.FloorNumber, s.Capacity, s.IsActive);

    private static string Describe(YogaStudio s) =>
        $"Name={s.StudioName}; Floor={s.FloorNumber}; Capacity={s.Capacity}; Active={s.IsActive}";

    private static Result<YogaStudioResponse> NotFound() =>
        Result<YogaStudioResponse>.Failure("Yoga studio not found.", "YOGA_STUDIO_NOT_FOUND", 404);

    private static Result<YogaStudioResponse> NameTaken() =>
        Result<YogaStudioResponse>.Failure("A studio with this name already exists in the gym.", "YOGA_STUDIO_NAME_TAKEN", 409);
}

/// <summary>Gym-local time helpers (Asia/Ho_Chi_Minh, UTC+7, no DST).</summary>
internal static class YogaTime
{
    public static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    public static DateTime LocalNow(TimeProvider clock) => clock.GetUtcNow().UtcDateTime + GymOffset;

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(LocalNow(clock));

    /// <summary>UTC instant of a gym-local date and time.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time) - GymOffset, DateTimeKind.Utc);
}
