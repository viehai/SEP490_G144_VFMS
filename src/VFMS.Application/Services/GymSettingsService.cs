using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-84 Manage General Policies: the gym's name, contact details, opening hours and whether it accepts new members.
/// Old and new values are written to the audit log.
/// </summary>
public sealed class GymSettingsService : IGymSettingsService
{
    private readonly IGymSettingsRepository _repository;

    public GymSettingsService(IGymSettingsRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GymSettingsResponse>> GetAsync(CancellationToken cancellationToken = default)
    {
        var gym = await _repository.GetDefaultGymAsync(cancellationToken);
        return gym is null ? NoGym() : Result<GymSettingsResponse>.Success(ToResponse(gym));
    }

    public async Task<Result<GymSettingsResponse>> UpdateAsync(
        GymSettingsRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (request.OpenTime.HasValue != request.CloseTime.HasValue ||
            request.OpenTime is { } open && request.CloseTime is { } close && open >= close)
            return Result<GymSettingsResponse>.Failure(
                "Opening time must be earlier than closing time.", "GYM_SETTINGS_INVALID_HOURS");

        var gym = await _repository.GetDefaultGymAsync(cancellationToken);
        if (gym is null)
            return NoGym();

        var before = Describe(gym);
        gym.GymName = string.Join(' ', request.GymName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        gym.Address = EquipmentService.Clean(request.Address);
        gym.PhoneNumber = EquipmentService.Clean(request.PhoneNumber);
        gym.Email = EquipmentService.Clean(request.Email)?.ToLowerInvariant();
        gym.OpenTime = request.OpenTime;
        gym.CloseTime = request.CloseTime;
        gym.IsAcceptingMembers = request.IsAcceptingMembers;
        var after = Describe(gym);
        if (before == after)
            return Result<GymSettingsResponse>.Success(ToResponse(gym)); // nothing changed, nothing to audit

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, nameof(Gym), ipAddress, before, after);
        await AuditedWriter.SaveChangedAsync(_repository, gym, audit, null, cancellationToken);
        return Result<GymSettingsResponse>.Success(ToResponse(gym));
    }

    private static string Describe(Gym g) =>
        $"Name={g.GymName}; Address={g.Address}; Phone={g.PhoneNumber}; Email={g.Email}; " +
        $"Open={g.OpenTime:HH\\:mm}; Close={g.CloseTime:HH\\:mm}; AcceptingMembers={g.IsAcceptingMembers}";

    private static GymSettingsResponse ToResponse(Gym g) =>
        new(g.Id, g.GymName, g.Address, g.PhoneNumber, g.Email, g.OpenTime, g.CloseTime, g.IsAcceptingMembers, g.UpdatedAt);

    private static Result<GymSettingsResponse> NoGym() =>
        Result<GymSettingsResponse>.Failure("No gym is configured in the system.", "GYM_NOT_CONFIGURED", 404);
}
