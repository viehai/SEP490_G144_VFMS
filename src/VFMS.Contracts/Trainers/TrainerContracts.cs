namespace VFMS.Contracts.Trainers;

public sealed record TrainerFileInput(string FileName, byte[] Content, string? ReplacesCertificateId = null);
public sealed record TrainerCertificateLinkInput(string Url, string? ReplacesCertificateId = null);
/// <summary>
/// TrainerRole (PT or Yoga) is required for Recruitment and ignored for ProfileUpdate, which keeps the hired role (UC 3.3).
/// Specialty is free text; a ProfileUpdate may leave it empty to keep the published one.
/// </summary>
public sealed record SubmitTrainerApplicationRequest(
    string FullName, string Email, string PhoneNumber, string CitizenId, DateOnly DateOfBirth,
    string Gender, string Specialty, int ExperienceYears, string Bio, bool Consent,
    List<TrainerFileInput> Certificates, string ApplicationType = "Recruitment", List<string>? CertificateUrls = null,
    List<TrainerCertificateLinkInput>? CertificateLinks = null, string? TrainerRole = null);
public sealed record TrainerApplicationReceipt(string TrackingCode, string Status, int SubmissionAttempt);
public sealed record TrainerRecruitmentStatus(bool IsAcceptingApplications);
public sealed record SetTrainerRecruitmentStatusRequest(bool IsAcceptingApplications);
public sealed record TrainerApplicationFilter(string? Status = null, string? TrainerRole = null,
    DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20, string? ApplicationType = null);
public sealed record ReviewTrainerApplicationRequest(string Action, string? Note, string? Level = null,
    string? ExistingUserId = null, bool IdentityVerified = false);
public sealed record UpdateTrainerProfileRequest(string? Bio = null, TrainerFileInput? Avatar = null);
public sealed record TrainerSetupRequest(string Token, string Password);
/// <summary>Search matches the trainer name or specialization; TrainerRole filters PT or Yoga (UC 3.4).</summary>
public sealed record TrainerSearch(string? Search = null, string? TrainerRole = null, string? Gender = null,
    decimal? MinimumRating = null, int Page = 1, int PageSize = 20);
/// <summary>Replaces: the published certificate (link or file name) a pending ProfileUpdate certificate would replace (UC 3.3).</summary>
public sealed record TrainerDocumentResponse(string Id, string FileName, string ContentType, string DownloadUrl, bool IsExternalLink = false,
    string? Replaces = null);
/// <summary>
/// HasUnfinishedPTContract blocks hiring the matched account; MemberServicesUntil warns that its Gym Membership or
/// locker rental can no longer be used once the account holds only the PT role (UC 3.2 step 7).
/// </summary>
public sealed record TrainerAccountMatch(string Status, string? UserId = null, string? FullName = null,
    IReadOnlyList<TrainerAccountCandidate>? Matches = null, bool HasUnfinishedPTContract = false,
    DateOnly? MemberServicesUntil = null);
public sealed record TrainerAccountCandidate(string UserId, string FullName, string Email, string PhoneNumber,
    bool EmailMatches, bool PhoneMatches, bool Eligible = true, bool HasUnfinishedPTContract = false,
    DateOnly? MemberServicesUntil = null);
public sealed record TrainerApplicationResponse(int Id, string TrackingCode, string Status, string FullName,
    string Email, string PhoneNumber, string CitizenId, DateOnly? DateOfBirth, string Gender, string Specialty,
    int ExperienceYears, string Bio, DateTime SubmittedAt, int SubmissionAttempt, string? ReviewNote,
    string? HiredUserId, IReadOnlyList<TrainerDocumentResponse> Certificates, string ApplicationType = "Recruitment",
    IReadOnlyList<TrainerSubmissionAttempt>? SubmissionHistory = null, string? InterviewNote = null, TrainerAccountMatch? AccountMatch = null,
    string? TrainerRole = null);
public sealed record TrainerSubmissionAttempt(int Id, int SubmissionAttempt, DateTime SubmittedAt, string Status);
public sealed record TrainerProfileResponse(int Id, string UserId, string FullName, string Level,
    string Specialty, string Gender, int ExperienceYears, string? Bio, string? AvatarUrl, decimal AverageRating)
{
    public TrainerProfileResponse() : this(0, "", "", "", "", "", 0, null, null, 0) { }
    public bool IsAcceptingNewMembers { get; init; }
    public DateOnly? NotAcceptingSince { get; init; }
    /// <summary>PT or Yoga. Only a PT sells packages and publishes a weekly timetable (UC 3.4).</summary>
    public string TrainerRole { get; init; } = "";
}
/// <summary>ContractsNotified: members told they may change trainer within one week (GBR-98).</summary>
public sealed record TrainerReadinessStatus(bool IsAcceptingNewMembers, int ContractsNotified);
public sealed record SetTrainerReadinessRequest(bool IsAcceptingNewMembers);
public sealed record TrainerReviewResponse(int Rating, string? Comment, string MemberName = "");
public sealed record TrainerPublicDetail(TrainerProfileResponse Profile, IReadOnlyList<TrainerDocumentResponse> Certificates,
    IReadOnlyList<TrainerReviewResponse> Reviews,
    bool HasActivePTContract = false, bool IsOwnProfile = false, int ReviewPage = 1, int ReviewTotal = 0);
public sealed record TrainerFileResponse(string FileName, string ContentType, byte[] Content);
