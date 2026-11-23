using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

/// <summary>An unsent trainer email ready for delivery: its application, the whole queue and the decrypted body
/// (null when the body can no longer be decrypted).</summary>
public sealed record TrainerQueuedMail(TrainerApplication Application, List<TrainerQueuedEmail> Queue,
    TrainerQueuedEmail Email, string? Body);

/// <summary>Data access for trainer recruitment, profiles and the public directory (UC 3.1 - 3.4, UC 4.12).</summary>
public interface ITrainerRepository : IAuditedRepository
{
    /// <summary>Runs the work under the recruitment lock in a serializable transaction.
    /// Saves and commits on success; rolls back on failure. Lock waits, deadlocks and unique-key races become a 409 result.</summary>
    Task<Result<T>> RunExclusiveAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default);
    void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity;

    // ── Lookups ──────────────────────────────────────────────────
    Task<bool> UserHasActiveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, string>> GetTrainerRoleNamesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveBannedWordsAsync(CancellationToken cancellationToken = default);
    Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAnyGymRecruitingAsync(CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<TrainerProfile?> GetProfileAsync(int id, CancellationToken cancellationToken = default);
    Task<TrainerProfile?> GetProfileByUserAsync(string userId, bool activeOnly, CancellationToken cancellationToken = default);
    Task<int?> GetActiveTrainerRoleIdAsync(string roleName, CancellationToken cancellationToken = default);
    /// <summary>The Trainer Role with its login role (ApplicationRole) loaded.</summary>
    Task<TrainerRole?> GetTrainerRoleAsync(int? id, CancellationToken cancellationToken = default);

    // ── Applications ─────────────────────────────────────────────
    Task<TrainerApplication?> GetApplicationAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Filtered page, newest first; ownerUserId limits it to the trainer's own applications.</summary>
    Task<(IReadOnlyList<TrainerApplication> Items, int TotalCount)> SearchApplicationsAsync(
        TrainerApplicationFilter filter, string? ownerUserId, CancellationToken cancellationToken = default);
    /// <summary>Applications of one type the user submitted or was hired through.</summary>
    Task<IReadOnlyList<TrainerApplication>> GetApplicationsByUserAsync(string applicationType, string userId,
        CancellationToken cancellationToken = default);
    /// <summary>GBR-03: Recruitment applications matching the account, email or phone; dotted Gmail variants count as one.</summary>
    Task<IReadOnlyList<TrainerApplication>> GetRecruitmentHistoryAsync(string email, string phoneNumber, int? applicationId, string? userId,
        CancellationToken cancellationToken = default);
    Task<TrainerApplication?> GetHiredRecruitmentAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Applications the user submitted, was hired through, or that use the user's email.</summary>
    Task<IReadOnlyList<TrainerApplication>> GetApplicationsLinkedToUserAsync(ApplicationUser user,
        CancellationToken cancellationToken = default);
    /// <summary>GBR-04: hired applications whose setup link expired unused and have no notification of that type since.</summary>
    Task<IReadOnlyList<TrainerApplication>> GetExpiredSetupLinkApplicationsAsync(DateTime now, string notificationType,
        CancellationToken cancellationToken = default);

    // ── Accounts ─────────────────────────────────────────────────
    /// <summary>GBR-107: Staff, Manager, Admin or active trainers matching email or phone.</summary>
    Task<bool> IsStaffOrActiveTrainerAsync(string email, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationUser>> FindMatchingAccountsAsync(string email, string? phoneNumber,
        CancellationToken cancellationToken = default);
    /// <summary>Users among the IDs holding any of the roles.</summary>
    Task<IReadOnlyList<string>> GetUsersWithAnyRoleAsync(IReadOnlyCollection<string> userIds, IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default);
    Task<bool> HasUnfinishedPTContractAsync(string userId, DateTime now, DateOnly makeUpFrom, CancellationToken cancellationToken = default);
    Task<DateOnly?> GetLatestGymMembershipEndAsync(string userId, DateOnly today, CancellationToken cancellationToken = default);
    Task<DateOnly?> GetLatestLockerRentalEndAsync(string userId, CancellationToken cancellationToken = default);
    Task<TrainerProfile?> FindProfileForHireAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> UserHasRoleIdAsync(string userId, int roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationUserRole>> GetRoleAssignmentsAsync(string userId, IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default);
    /// <summary>The login role ID of an active Trainer Role, when it is one of the given login roles.</summary>
    Task<int?> GetActiveLoginRoleIdAsync(int trainerRoleId, IReadOnlyCollection<string> loginRoles,
        CancellationToken cancellationToken = default);
    /// <summary>A pending, unbanned account holding only the given login role.</summary>
    Task<ApplicationUser?> GetPendingTrainerAccountAsync(string userId, int loginRoleId, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> FindEligiblePasswordSetupAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default);
    Task<bool> TryCompletePasswordSetupAsync(string userId, string tokenHash, string passwordHash, DateTime now,
        CancellationToken cancellationToken = default);

    // ── Public directory (UC 3.4) ────────────────────────────────
    Task<(IReadOnlyList<TrainerProfileResponse> Items, int TotalCount)> SearchPublicProfilesAsync(TrainerSearch search,
        CancellationToken cancellationToken = default);
    Task<TrainerProfileResponse?> GetPublicProfileAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> HasRunningContractWithTrainerAsync(string memberId, string trainerUserId, DateOnly today,
        CancellationToken cancellationToken = default);
    Task<int> CountPublishedReviewsAsync(string trainerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainerReviewResponse>> GetPublishedReviewsAsync(string trainerUserId, int skip, int take,
        CancellationToken cancellationToken = default);

    // ── Readiness (UC 4.12) ──────────────────────────────────────
    Task<bool> HasRunningPTContractAsync(string trainerUserId, DateOnly today,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PTPurchase>> GetContractsWithTrainerChangeDeadlineAsync(string trainerUserId,
        CancellationToken cancellationToken = default);
    /// <summary>Active contracts of the trainer with no session taught, completed or disputed yet.</summary>
    Task<IReadOnlyList<PTPurchase>> GetUntouchedRunningContractsAsync(string trainerUserId, DateOnly today,
        CancellationToken cancellationToken = default);

    // ── Files and email queue ────────────────────────────────────
    /// <summary>Stores the file privately; it is deleted again if the surrounding exclusive work does not commit.</summary>
    Task<TrainerStoredFile> SaveFileAsync(TrainerFileInput file, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadFileAsync(TrainerStoredFile file, CancellationToken cancellationToken = default);
    /// <summary>Adds an email to the application's queue with an encrypted body.</summary>
    void QueueEmail(TrainerApplication application, string recipient, string subject, string body);
    void RemoveUnsentEmails(TrainerApplication application, string subject);
    /// <summary>Runs one delivery step under the recruitment lock without waiting; throws when the lock is busy.</summary>
    Task RunMailDeliveryAsync(Func<Task> work, CancellationToken cancellationToken = default);
    /// <summary>The next unsent email due by now, preferring the given subject.</summary>
    Task<TrainerQueuedMail?> GetNextQueuedEmailAsync(DateTime now, string prioritySubject, CancellationToken cancellationToken = default);
    void UpdateEmailQueue(TrainerApplication application, IReadOnlyList<TrainerQueuedEmail> queue);
}

public interface ITrainerService
{
    Task<Result<TrainerRecruitmentStatus>> RecruitmentStatus(CancellationToken ct);
    Task<Result<TrainerRecruitmentStatus>> SetRecruitmentStatus(bool accepting, string actor, CancellationToken ct);
    Task<Result<TrainerApplicationReceipt>> Submit(SubmitTrainerApplicationRequest request, string? actor, CancellationToken ct);
    Task<Result<PagedResponse<TrainerApplicationResponse>>> Applications(TrainerApplicationFilter filter, string actor, CancellationToken ct);
    Task<Result<PagedResponse<TrainerApplicationResponse>>> MyApplications(TrainerApplicationFilter filter, string actor, CancellationToken ct);
    Task<Result<TrainerApplicationResponse>> Application(int id, string actor, CancellationToken ct);
    Task<Result<TrainerApplicationResponse>> Review(int id, ReviewTrainerApplicationRequest request, string actor, CancellationToken ct);
    Task<Result<bool>> ResendSetup(int applicationId, string actor, CancellationToken ct);
    Task<Result<bool>> Setup(TrainerSetupRequest request, CancellationToken ct);
    Task<Result<TrainerReadinessStatus>> SetReadiness(bool accepting, string actor, CancellationToken ct);
    Task<Result<PagedResponse<TrainerProfileResponse>>> Search(TrainerSearch search, CancellationToken ct);
    Task<Result<TrainerPublicDetail>> Public(int id, CancellationToken ct, string? actor = null, int reviewPage = 1);
    Task<Result<TrainerPublicDetail>> Mine(string actor, CancellationToken ct);
    Task<Result<bool>> Update(UpdateTrainerProfileRequest request, string actor, CancellationToken ct);
    Task<Result<TrainerFileResponse>> ApplicationDocument(int id, string fileId, string? actor, CancellationToken ct);
    Task<Result<TrainerFileResponse>> Document(int id, string fileId, string? actor, CancellationToken ct);
    Task<Result<TrainerFileResponse>> Avatar(int id, string? actor, CancellationToken ct);
    /// <summary>GBR-04: tells Managers in-app, once per expired link, that a hired trainer's setup link expired unused.</summary>
    Task<Result<int>> NotifyExpiredSetupLinks(CancellationToken ct);
    /// <summary>Sends at most one queued trainer email; false when email delivery is not configured.</summary>
    Task<bool> DeliverQueuedEmail(IEmailService email, CancellationToken ct);
}
