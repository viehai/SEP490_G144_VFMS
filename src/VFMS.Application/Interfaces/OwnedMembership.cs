using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public sealed record OwnedMembership(
    int MembershipId, int MembershipPlanId, string PlanName, ComponentType ComponentType,
    DateOnly StartDate, DateOnly EndDate, MembershipStatus Status, DateTime? PurchasedAt = null)
{
    public int PackageComponentId { get; init; }
    public string? ComponentTermsSnapshot { get; init; }
}

public sealed record OwnedMembershipDetail(
    OwnedMembership Membership, DateTime? PurchasedAt, string? InvoiceNumber,
    int TotalFrozenDays, int FreezeCount, IReadOnlyList<OwnedMembershipFreeze> FreezeHistory,
    int? GrantedFreezeAllowanceDays = null, int? GrantedFreezeCount = null, int? GrantedMinFreezeDays = null, int? GrantedMaxFreezeDays = null);

public sealed record OwnedMembershipFreeze(
    DateOnly RequestedStartDate, DateOnly RequestedEndDate, DateOnly? ActualStartDate,
    DateOnly? ActualEndDate, FreezeRequestStatus Status, DateTime? SubmittedAt = null, string? ReviewNote = null);
