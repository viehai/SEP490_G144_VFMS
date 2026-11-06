using VFMS.Application.Interfaces;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-43/UC-52 Manage Face Recognition Profile. Requires explicit biometric consent; stores only the encrypted
/// descriptor; deleting erases the template immediately (SRS 2.3 PDPL data minimisation).
/// </summary>
public sealed class FaceProfileService : IFaceProfileService
{
    private const string Entity = nameof(FaceData);

    private readonly ICheckInRepository _repository;
    private readonly IFaceTemplateProtector _protector;
    private readonly TimeProvider _clock;

    public FaceProfileService(ICheckInRepository repository, IFaceTemplateProtector protector, TimeProvider clock)
    {
        _repository = repository;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<FaceProfileResponse>> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        Result<FaceProfileResponse>.Success(ToResponse(await _repository.GetFaceDataAsync(userId, cancellationToken)));

    public async Task<Result<FaceProfileResponse>> RegisterAsync(
        string userId, RegisterFaceRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!request.ConsentToBiometricProcessing)
            return Result<FaceProfileResponse>.Failure(
                "You must agree to facial biometric processing before registering your face.", "FACE_CONSENT_REQUIRED");

        var now = _clock.GetUtcNow().UtcDateTime;
        var template = _protector.Protect(request.Descriptor);
        var face = await _repository.GetFaceDataAsync(userId, cancellationToken);
        var isNew = face is null;
        face ??= new FaceData { UserId = userId };

        face.FaceNetEmbedding = template;
        face.IsActive = true;
        face.IsDeleted = false;
        face.RegisteredAt = now;
        face.ConsentGrantedAt = now;
        face.LastUsedAt = null;
        face.UpdatedAt = now;

        // Audit without the template itself.
        var audit = AuditedWriter.Audit(userId, isNew ? AuditActionType.Create : AuditActionType.Update, Entity, ipAddress,
            newValue: "Face template registered with biometric consent.");
        if (isNew)
            await AuditedWriter.SaveNewAsync(_repository, face, audit, null, cancellationToken);
        else
            await AuditedWriter.SaveChangedAsync(_repository, face, audit, null, cancellationToken);
        return Result<FaceProfileResponse>.Success(ToResponse(face), isNew ? 201 : 200);
    }

    public async Task<Result<FaceProfileResponse>> DeleteAsync(string userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var face = await _repository.GetFaceDataAsync(userId, cancellationToken);
        if (face is null || !face.IsActive)
            return Result<FaceProfileResponse>.Failure("No face profile is registered.", "FACE_NOT_REGISTERED", 404);

        face.FaceNetEmbedding = [];   // erase the biometric template, keep the row for the audit trail
        face.IsActive = false;
        face.IsDeleted = true;
        face.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        var audit = AuditedWriter.Audit(userId, AuditActionType.Delete, Entity, ipAddress, oldValue: "Face template erased by user.");
        await AuditedWriter.SaveChangedAsync(_repository, face, audit, null, cancellationToken);
        return Result<FaceProfileResponse>.Success(ToResponse(null));
    }

    private static FaceProfileResponse ToResponse(FaceData? f) =>
        f is { IsActive: true, IsDeleted: false } && f.FaceNetEmbedding.Length > 0
            ? new FaceProfileResponse(true, f.RegisteredAt, f.LastUsedAt, f.ConsentGrantedAt)
            : new FaceProfileResponse(false, null, null, null);
}

/// <summary>UC-48/UC-57 View Active Check-ins: who is inside now and current occupancy.</summary>
public sealed class ActiveCheckInService : IActiveCheckInService
{
    public const int DefaultPeakThreshold = 100;

    private readonly ICheckInRepository _repository;
    private readonly TimeProvider _clock;
    private readonly int _peakThreshold;

    public ActiveCheckInService(ICheckInRepository repository, TimeProvider clock, int peakThreshold = DefaultPeakThreshold)
    {
        _repository = repository;
        _clock = clock;
        _peakThreshold = peakThreshold;
    }

    public async Task<Result<ActiveCheckInsResponse>> GetActiveAsync(string? search, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var active = await _repository.GetActiveCheckInsAsync(cancellationToken);
        var gymMidnightUtc = (now + YogaTime.GymOffset).Date - YogaTime.GymOffset;
        var today = await _repository.CountCheckInsSinceAsync(DateTime.SpecifyKind(gymMidnightUtc, DateTimeKind.Utc), cancellationToken);

        var term = search?.Trim();
        var items = active
            .Where(c => string.IsNullOrEmpty(term) ||
                        c.Member.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        c.Member.PhoneNumber.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.CheckinTime)
            .Select(c => new ActiveCheckInResponse(
                c.Id, c.MemberUserId, c.Member.FullName, c.Member.PhoneNumber, c.Member.AvatarUrl, c.CheckinTime,
                (int)Math.Max(0, (now - c.CheckinTime).TotalMinutes), c.CheckinMethod.ToString(), c.HandledByUser?.FullName))
            .ToArray();
        return Result<ActiveCheckInsResponse>.Success(
            new ActiveCheckInsResponse(active.Count, today, items, _peakThreshold, active.Count >= _peakThreshold));
    }
}
