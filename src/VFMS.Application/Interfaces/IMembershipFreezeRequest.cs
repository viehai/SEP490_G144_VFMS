using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IMembershipFreezeRequestService
{
    Task<Result<MembershipFreezeEligibilityResponse>> GetEligibilityAsync(
        string memberId, int membershipId, int? excludeRequestId, CancellationToken ct);
    Task<Result<IReadOnlyList<MembershipFreezeRequestResponse>>> ListAsync(string memberId, CancellationToken ct);
    Task<Result<MembershipFreezeRequestResponse>> GetAsync(string memberId, int requestId, CancellationToken ct);
    Task<Result<MembershipFreezeRequestResponse>> CreateAsync(
        string memberId, MembershipFreezeRequest request, CancellationToken ct);
    Task<Result<MembershipFreezeRequestResponse>> UpdateAsync(
        string memberId, int requestId, MembershipFreezeRequest request, CancellationToken ct);
    Task<Result<MembershipFreezeRequestResponse>> CancelAsync(string memberId, int requestId, CancellationToken ct);
    Task<Result<MembershipFreezeRequestResponse>> RequestEarlyUnfreezeAsync(
        string memberId, int requestId, CancellationToken ct);
}

public interface IMembershipFreezeRequestRepository : IAuditedRepository
{
    Task<MemberMembership?> GetOwnedMembershipAsync(
        string memberId, int membershipId, bool forUpdate, CancellationToken ct);
    Task<List<MembershipFreezeReadModel>> ListOwnedRequestsAsync(string memberId, CancellationToken ct);
    Task<MembershipFreezeReadModel?> FindOwnedRequestAsync(string memberId, int requestId, CancellationToken ct);
}

public sealed record MembershipFreezeReadModel(
    MembershipFreeze Request, string PlanName, MembershipStatus MembershipStatus);
