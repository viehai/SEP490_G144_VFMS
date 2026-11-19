using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Packages;

namespace VFMS.Application.Interfaces;

public interface IMembershipPlanService
{
    Task<Result<PagedResponse<MembershipPlanResponse>>> ListAsync(
        MembershipPlanListRequest request, CancellationToken cancellationToken);
    Task<Result<MembershipPlanResponse>> GetAsync(int id, CancellationToken cancellationToken);
    Task<Result<PagedResponse<MembershipPlanCatalogItemResponse>>> ListCatalogAsync(
        MembershipPlanListRequest request, CancellationToken cancellationToken);
    Task<Result<MembershipPlanCatalogItemResponse>> GetCatalogItemAsync(int id, CancellationToken cancellationToken);
    Task<Result<MembershipPlanResponse>> CreateAsync(
        MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken);
    Task<Result<MembershipPlanResponse>> UpdateAsync(
        int id, MembershipPlanRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken);
    Task<Result<MembershipPlanResponse>> ChangeStatusAsync(
        int id, MembershipPlanStatusRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken);
    Task<Result<MembershipPlanDeleteResponse>> DeleteAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Public catalog: trả về các package Active theo ComponentType.
    /// Anonymous access — không yêu cầu xác thực.
    /// </summary>
    Task<Result<PagedResponse<PackageCatalogItemResponse>>> ListPackageCatalogAsync(
        PackageCatalogQuery request, CancellationToken cancellationToken);
}
