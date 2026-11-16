using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>Shared eligibility for future entrance and class-booking consumers. Checkout has its own lifecycle.</summary>
public interface IMembershipAccessService
{
    Task<Result<bool>> CheckGymEntryAsync(string memberId, int membershipId, CancellationToken ct);
    Task<Result<bool>> CheckClassBookingAsync(string memberId, int membershipId, CancellationToken ct);
}

public interface IMembershipAccessRepository
{
    Task<OwnedMembershipAccess?> FindOwnedAsync(string memberId, int membershipId, CancellationToken ct);
}

public sealed record OwnedMembershipAccess(
    int MembershipId, ComponentType ComponentType, MembershipStatus Status, DateOnly StartDate, DateOnly EndDate);
