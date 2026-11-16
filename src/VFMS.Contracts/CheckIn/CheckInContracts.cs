namespace VFMS.Contracts.CheckIn;

// ── Face recognition profile (UC-43 / UC-52) ────────────────────

/// <summary>
/// Register or replace the caller's face template. The browser computes the 128-number face descriptor;
/// the photo itself never leaves the device. ConsentToBiometricProcessing must be true (PDPL).
/// </summary>
public sealed record RegisterFaceRequest(IReadOnlyList<float> Descriptor, bool ConsentToBiometricProcessing);

public sealed record FaceProfileResponse(bool IsRegistered, DateTime? RegisteredAt, DateTime? LastUsedAt, DateTime? ConsentGrantedAt);

// ── Kiosk check-in / check-out (UC-44, UC-45) ───────────────────

/// <summary>A kiosk scan: either the 128-number face descriptor computed in the kiosk browser, or a member's QR token.</summary>
public sealed record KioskScanRequest(IReadOnlyList<float>? Descriptor, string? QrToken);

/// <summary>MSG-35 data. CompletedPtSessionId is set when the entry auto-completed a PT session (GBR-92, MSG-38).</summary>
public sealed record CheckInResultResponse(
    int CheckInId, string MemberUserId, string MemberName, DateOnly ValidThrough, DateTime CheckinTime,
    string Message, int? CompletedPtSessionId);

public sealed record CheckOutResultResponse(
    int CheckInId, string MemberUserId, string MemberName, DateTime CheckinTime, DateTime CheckoutTime, int MinutesInside);

/// <summary>One kiosk scan: Action is "CheckIn" (CheckIn set) or "CheckOut" (CheckOut set), decided by whether the member is inside.</summary>
public sealed record KioskScanResultResponse(string Action, CheckInResultResponse? CheckIn, CheckOutResultResponse? CheckOut);

// ── Front desk (UC-46, UC-47) ───────────────────────────────────

/// <summary>A member found at the front desk. MembershipStatus is "Active" when they may enter today.</summary>
public sealed record FrontDeskMemberResponse(
    string MemberUserId, string FullName, string? PhoneNumber, string MembershipStatus, DateOnly? ValidThrough, bool IsCheckedIn);

public sealed record ManualCheckInRequest(string MemberUserId);

/// <summary>Short-lived single-use token the member shows as a QR code at the kiosk or yoga studio.</summary>
public sealed record MemberQrResponse(string Token, DateTime ExpiresAt);

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

/// <summary>IsPeak: current occupancy reached the configured peak-hour threshold (feature list UC-48).</summary>
public sealed record ActiveCheckInsResponse(
    int CurrentOccupancy, int CheckInsToday, IReadOnlyList<ActiveCheckInResponse> Items, int PeakThreshold = 0, bool IsPeak = false);
