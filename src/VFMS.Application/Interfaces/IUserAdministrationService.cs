using VFMS.Contracts.Common;
using VFMS.Contracts.Users;

namespace VFMS.Application.Interfaces;

public interface IUserAdministrationService
{
    Task<Result<WalkInCustomerResponse>> CreateWalkInCustomerAsync(
        WalkInCustomerRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<UserProfileResponse>> GetMyProfileAsync(
        string userId,
        CancellationToken cancellationToken = default);
    Task<Result<UserProfileResponse>> UpdateMyProfileAsync(
        string userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<bool>> ChangeMyPasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<PagedUsersResponse>> GetUsersAsync(
        UserListRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<UserAccountResponse>> CreateStaffAsync(
        CreateStaffRequest request,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<UserAccountResponse>> UpdateRolesAsync(
        string userId,
        UpdateUserRolesRequest request,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<UserAccountResponse>> LockUserAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<UserAccountResponse>> UnlockUserAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
