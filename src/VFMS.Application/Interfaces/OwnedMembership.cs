using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public sealed record OwnedMembership(
    int MembershipId, int MembershipPlanId, string PlanName, ComponentType ComponentType,
    DateOnly StartDate, DateOnly EndDate, MembershipStatus Status);

public sealed record OwnedMembershipDetail(
    OwnedMembership Membership, DateTime? PurchasedAt, string? InvoiceNumber,
    int TotalFrozenDays, int FreezeCount, IReadOnlyList<OwnedMembershipFreeze> FreezeHistory);

public sealed record OwnedMembershipFreeze(
    DateOnly RequestedStartDate, DateOnly RequestedEndDate, DateOnly? ActualStartDate,
    DateOnly? ActualEndDate, FreezeRequestStatus Status);
