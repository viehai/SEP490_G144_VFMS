namespace VFMS.Contracts.Equipment;

// ── View Gym Details & Facilities (UC-70) ────────────────────────

/// <summary>
/// What a guest, customer or member sees about the gym (UC-70): contact details, opening hours (gym local time),
/// facilities and the general membership freeze policy. Membership prices come from the public plan catalog.
/// </summary>
public sealed record GymDetailsResponse(
    string GymName,
    string? Address,
    string? PhoneNumber,
    string? Email,
    TimeOnly? OpenTime,
    TimeOnly? CloseTime,
    bool IsAcceptingMembers,
    IReadOnlyList<GymEquipmentAreaResponse> EquipmentAreas,
    IReadOnlyList<GymStudioResponse> YogaStudios,
    IReadOnlyList<GymLockerZoneResponse> LockerZones,
    GymFreezePolicySummary? FreezePolicy);

/// <summary>
/// One equipment category. <see cref="Total"/> counts machines in use (not retired);
/// <see cref="Available"/> counts the ones working right now. A null name means equipment without a category.
/// </summary>
public sealed record GymEquipmentAreaResponse(string? CategoryName, string? Description, int Total, int Available);

public sealed record GymStudioResponse(string StudioName, int FloorNumber, int Capacity);

/// <summary><see cref="GenderRestriction"/> is None, Male or Female.</summary>
public sealed record GymLockerZoneResponse(
    string ZoneName, int FloorNumber, string GenderRestriction, int TotalLockers, string? Description);

/// <summary>The membership freeze rules a member can count on, without the Manager-only fields.</summary>
public sealed record GymFreezePolicySummary(
    int MinimumFreezeDays,
    int MaximumFreezeDays,
    int MinimumRequestDays,
    int EligibilityPaidDays,
    int? DefaultMaxFreezeCount = null);
