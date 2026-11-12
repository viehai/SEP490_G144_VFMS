using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IMembershipFreezeReviewService
{
    Task<Result<PagedResponse<MembershipFreezeReviewSummaryResponse>>> ListAsync(MembershipFreezeReviewListRequest request, CancellationToken ct);
    Task<Result<MembershipFreezeReviewDetailResponse>> GetAsync(int requestId, string type, CancellationToken ct);
    Task<Result<MembershipFreezeReviewDetailResponse>> ApproveAsync(string managerId, int requestId, bool early, string? ipAddress, CancellationToken ct);
    Task<Result<MembershipFreezeReviewDetailResponse>> RejectAsync(string managerId, int requestId, bool early, RejectMembershipFreezeRequest request, string? ipAddress, CancellationToken ct);
}

public interface IMembershipFreezeReviewRepository : IAuditedRepository
{
    Task<(IReadOnlyList<MembershipFreezeReviewReadModel> Items, int TotalCount)> SearchAsync(string? type, int page, int pageSize, CancellationToken ct);
    Task<MembershipFreezeReviewReadModel?> FindAsync(int requestId, bool early, CancellationToken ct);
}

public sealed record MembershipFreezeReviewReadModel(
    MembershipFreeze Request, string MemberName, string MemberEmail, string PlanName, MemberMembership Membership);
