using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>Eligibility of an individual membership for gym entry and class booking.</summary>
public interface IMembershipAccessService
{
    Task<Result<bool>> CheckGymEntryAsync(string memberId, int membershipId, CancellationToken ct);
    Task<Result<bool>> CheckClassBookingAsync(string memberId, int membershipId, CancellationToken ct);
}

public interface IMembershipAccessRepository
{
    /// <summary>Gym, Yoga and PT plans for the specified members, including standalone PT purchases.</summary>
    Task<IReadOnlyList<EntryPlan>> GetEntryPlansAsync(IReadOnlyList<string> userIds, DateOnly today, DateTime nowUtc,
        CancellationToken cancellationToken = default);
    Task<OwnedMembershipAccess?> FindOwnedAsync(string memberId, int membershipId, CancellationToken ct);
}

/// <summary>A membership or PT contract as it stands on the requested gym day.</summary>
public sealed record EntryPlan(string MemberUserId, int? MemberMembershipId, string Status, DateOnly? ValidThrough)
{
    public bool IsActive => Status == "Active";

    public static EntryPlan? Best(IEnumerable<EntryPlan> plans) =>
        plans.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.ValidThrough)
            .ThenByDescending(p => p.MemberMembershipId).FirstOrDefault();
}

public sealed record OwnedMembershipAccess(
    int MembershipId, ComponentType ComponentType, MembershipStatus Status, DateOnly StartDate, DateOnly EndDate);
