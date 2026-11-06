namespace VFMS.Contracts.Trainers;

public sealed record TrainerFileInput(string FileName, byte[] Content);
public sealed record SubmitTrainerApplicationRequest(
    string FullName, string Email, string PhoneNumber, string CitizenId, DateOnly DateOfBirth,
    string Gender, string Specialty, int ExperienceYears, string Bio, bool Consent,
    List<TrainerFileInput> Certificates);
public sealed record TrainerApplicationReceipt(string TrackingCode, string Status, int SubmissionAttempt);
public sealed record TrainerApplicationFilter(string? Status = null, string? Specialty = null,
    DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20);
public sealed record ReviewTrainerApplicationRequest(string Action, string Note, string? Level = null,
    string? CommissionTier = null, string? ExistingUserId = null, bool IdentityVerified = false);
public sealed record RecruitmentRestrictionRequest(string Email, string CitizenId, bool IsActive, string Reason);
public sealed record UpdateTrainerProfileRequest(string Bio, TrainerFileInput? Avatar = null);
public sealed record TrainerCredentialRequest(string Specialty, List<TrainerFileInput> Certificates);
public sealed record ReviewTrainerCredentialRequest(bool Approve, string Note);
public sealed record TrainerSetupRequest(string Token, string Password);
public sealed record TrainerSearch(string? Search = null, string? Specialty = null, string? Gender = null,
    decimal? MinimumRating = null, int Page = 1, int PageSize = 20);
public sealed record TrainerDocumentResponse(int Id, string FileName, string ContentType);
public sealed record TrainerApplicationResponse(int Id, string TrackingCode, string Status, string FullName,
    string Email, string PhoneNumber, string CitizenId, DateOnly? DateOfBirth, string Gender, string Specialty,
    int ExperienceYears, string Bio, DateTime SubmittedAt, int SubmissionAttempt, string? ReviewNote,
    string? HiredUserId, IReadOnlyList<TrainerDocumentResponse> Certificates);
public sealed record TrainerProfileResponse(int Id, string UserId, string FullName, string Level,
    string Specialty, string Gender, int ExperienceYears, string? Bio, int? AvatarFileId, decimal AverageRating)
{
    public TrainerProfileResponse() : this(0, "", "", "", "", "", 0, null, null, 0) { }
}
public sealed record TrainerAvailabilityResponse(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Status);
public sealed record TrainerReviewResponse(int Rating, string? Comment);
public sealed record TrainerPublicDetail(TrainerProfileResponse Profile, IReadOnlyList<TrainerDocumentResponse> Certificates,
    IReadOnlyList<TrainerAvailabilityResponse> Availability, IReadOnlyList<TrainerReviewResponse> Reviews);
public sealed record TrainerCredentialResponse(int Id, int TrainerProfileId, string Specialty, string Status,
    string? ReviewNote, DateTime CreatedAt, IReadOnlyList<TrainerDocumentResponse> Certificates);
public sealed record TrainerFileResponse(string FileName, string ContentType, byte[] Content);
