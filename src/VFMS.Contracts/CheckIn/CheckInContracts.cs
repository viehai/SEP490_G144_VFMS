namespace VFMS.Contracts.CheckIn;

// ── Face recognition profile (UC-43 / UC-52) ────────────────────

/// <summary>
/// Register or replace the caller's face template. The browser computes the 128-number face descriptor;
/// the photo itself never leaves the device. ConsentToBiometricProcessing must be true (PDPL).
/// </summary>
public sealed record RegisterFaceRequest(IReadOnlyList<float> Descriptor, bool ConsentToBiometricProcessing);

public sealed record FaceProfileResponse(bool IsRegistered, DateTime? RegisteredAt, DateTime? LastUsedAt, DateTime? ConsentGrantedAt);

// ── Active check-ins (UC-48 / UC-57) ────────────────────────────

public sealed record ActiveCheckInResponse(
    int CheckInId,
    string MemberUserId,
    string MemberName,
    string? PhoneNumber,
    string? AvatarUrl,
    DateTime CheckinTime,
    int MinutesInside,
    string Method,
    string? HandledByName
);

public sealed record ActiveCheckInsResponse(int CurrentOccupancy, int CheckInsToday, IReadOnlyList<ActiveCheckInResponse> Items);
