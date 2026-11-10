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

    /// <summary>All registered face templates with their users (tracked, so LastUsedAt can be set).</summary>
    Task<IReadOnlyList<FaceData>> GetActiveFaceDataAsync(CancellationToken cancellationToken = default);
    /// <summary>Active QR codes: of one user, or the one with this token (tracked).</summary>
    Task<IReadOnlyList<QRCode>> GetActiveQrCodesAsync(string? userId, string? token, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The member's memberships with their package component (no tracking).</summary>
    Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The member's open check-in (tracked), if any.</summary>
    Task<CheckIn?> GetOpenCheckInAsync(string userId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastCheckInTimeAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The member's Scheduled PT sessions on a gym-local date, with their purchase (tracked).</summary>
    Task<IReadOnlyList<PTSession>> GetScheduledPtSessionsAsync(string userId, DateOnly date, CancellationToken cancellationToken = default);
    /// <summary>Up to 20 users whose name or phone contains the term, or who hold a membership bought on that order number;
    /// with their memberships (component loaded) and whether they have an open visit.</summary>
    Task<IReadOnlyList<(ApplicationUser User, IReadOnlyList<MemberMembership> Memberships, bool IsCheckedIn)>> SearchMembersAsync(
        string term, CancellationToken cancellationToken = default);
}

/// <summary>Issues and redeems the member's short-lived QR token (kiosk UC-44/45, yoga attendance UC-42).</summary>
public interface IMemberQrService
{
    Task<Result<MemberQrResponse>> IssueAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The member who owns this unexpired token; the token is used up.</summary>
    Task<string?> RedeemAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>UC-44 Self Check-In and UC-46 Manual Check-In.</summary>
public interface ICheckInService
{
    Task<Result<CheckInResultResponse>> KioskCheckInAsync(KioskScanRequest request, CancellationToken cancellationToken = default);
    /// <summary>UC-46: members by name, phone or membership (order) number, with today's entry status.</summary>
    Task<Result<IReadOnlyList<FrontDeskMemberResponse>>> SearchMembersAsync(string? search, CancellationToken cancellationToken = default);
    Task<Result<CheckInResultResponse>> ManualCheckInAsync(string memberUserId, string staffUserId, CancellationToken cancellationToken = default);
    /// <summary>UC-45 Self Check-Out.</summary>
    Task<Result<CheckOutResultResponse>> KioskCheckOutAsync(KioskScanRequest request, CancellationToken cancellationToken = default);
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
