using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>Class opening requests (GBR-128..GBR-130).</summary>
public interface IYogaClassOpeningRepository : IAuditedRepository
{
    /// <summary>Tracked request with instructor, type and preferred studio loaded.</summary>
    Task<YogaClassOpeningRequest?> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Newest first. Null filters mean "any".</summary>
    Task<IReadOnlyList<YogaClassOpeningRequest>> ListAsync(
        string? instructorUserId, YogaClassRequestStatus? status, CancellationToken cancellationToken = default);
    Task<int> CountPendingAsync(string instructorUserId, CancellationToken cancellationToken = default);
    /// <summary>Exclusive lock on one request until the current transaction ends, so two Managers can't decide it twice.</summary>
    Task LockAsync(int id, CancellationToken cancellationToken = default);
}

public interface IYogaClassOpeningService
{
    Task<Result<YogaClassOpeningOptionsResponse>> GetOptionsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<YogaClassOpeningRequestResponse>>> GetMineAsync(string instructorUserId, CancellationToken cancellationToken = default);
    Task<Result<YogaClassOpeningRequestResponse>> SubmitAsync(
        string instructorUserId, YogaClassOpeningRequestRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassOpeningRequestResponse>> WithdrawAsync(
        string instructorUserId, int id, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<YogaClassOpeningRequestResponse>>> GetAllAsync(string? status, CancellationToken cancellationToken = default);
    /// <summary>Creates the Draft class from the Manager's reviewed form and marks the request Approved, in one transaction.</summary>
    Task<Result<YogaClassResponse>> ApproveAsync(
        int id, YogaClassRequest classForm, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassOpeningRequestResponse>> RejectAsync(
        int id, string reason, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
