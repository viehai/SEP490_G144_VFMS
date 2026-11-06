using VFMS.Contracts.CheckIn;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface ICheckInRepository : IAuditedRepository
{
    /// <summary>The user's face record, including inactive ones (tracked).</summary>
    Task<FaceData?> GetFaceDataAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Open check-ins with member and staff loaded.</summary>
    Task<IReadOnlyList<CheckIn>> GetActiveCheckInsAsync(CancellationToken cancellationToken = default);
    Task<int> CountCheckInsSinceAsync(DateTime utcFrom, CancellationToken cancellationToken = default);
}

/// <summary>Encrypts face templates at rest (SRS 2.3: only encrypted embeddings are stored).</summary>
public interface IFaceTemplateProtector
{
    byte[] Protect(IReadOnlyList<float> descriptor);
    float[] Unprotect(byte[] protectedTemplate);
}

/// <summary>UC-43/UC-52 Manage Face Recognition Profile.</summary>
public interface IFaceProfileService
{
    Task<Result<FaceProfileResponse>> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<FaceProfileResponse>> RegisterAsync(string userId, RegisterFaceRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<FaceProfileResponse>> DeleteAsync(string userId, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>UC-48/UC-57 View Active Check-ins.</summary>
public interface IActiveCheckInService
{
    Task<Result<ActiveCheckInsResponse>> GetActiveAsync(string? search, CancellationToken cancellationToken = default);
}
