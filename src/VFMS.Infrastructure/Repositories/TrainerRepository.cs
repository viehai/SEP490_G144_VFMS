using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class TrainerRepository(VfmsDbContext db, IDataProtectionProvider protection, IConfiguration? configuration = null)
    : AuditedRepositoryBase(db), ITrainerRepository
{
    private const string GmailSuffix = "@gmail.com";
    private const string YogaLoginRole = "YogaInstructor";
    private const string RecruitmentLock = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment', @LockMode='Exclusive', @LockOwner='Transaction', ";
    private static readonly string[] StaffRoles = ["Staff", "Manager", "Admin"];

    private readonly List<string> pendingFiles = [];
    private IDataProtector EmailProtector => protection.CreateProtector("VFMS.TrainerEmail.v1");
    private string StorageRoot => Path.GetFullPath(configuration?["TrainerFiles:Root"] ??
        Path.Combine(AppContext.BaseDirectory, "App_Data", "trainer-files"));

    public void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity => Context.Remove((object)entity);

    public async Task<Result<T>> RunExclusiveAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // Recruitment volume is small; one transaction-scoped SQL lock covers cross-identity cooldown and onboarding races.
        try
        {
            await Context.Database.ExecuteSqlRawAsync(RecruitmentLock + "@LockTimeout=10000; IF @r < 0 THROW 51000, 'Recruitment busy', 1;", cancellationToken);
            var result = await work();
            if (result.IsSuccess)
            {
                await Context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                pendingFiles.Clear();
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                Context.ChangeTracker.Clear();
            }
            return result;
        }
        catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) is 51000 or 1205)
        {
            if (AccountIdentityConflict.SqlErrorNumber(exception) == 51000)
                await transaction.RollbackAsync(cancellationToken);
            Context.ChangeTracker.Clear();
            return Result<T>.Failure("Hệ thống tuyển dụng đang bận. Vui lòng thử lại sau ít phút.", "TRAINER_CONFLICT", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            Context.ChangeTracker.Clear();
            return Result<T>.Failure("Hồ sơ vừa thay đổi. Vui lòng tải lại và thử lại.", "TRAINER_CONFLICT", 409);
        }
        catch (DbUpdateException exception) when (AccountIdentityConflict.GetField(exception) is { } field)
        {
            await transaction.RollbackAsync(cancellationToken);
            Context.ChangeTracker.Clear();
            return AccountIdentityConflict.ResultFor<T>(field);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            Context.ChangeTracker.Clear();
            return Result<T>.Failure("Không thể lưu do dữ liệu bị xung đột. Vui lòng tải lại và thử lại.", "TRAINER_CONFLICT", 409);
        }
        finally
        {
            foreach (var path in pendingFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Orphaned files can be removed during storage maintenance.
                }
            }
            pendingFiles.Clear();
        }
    }

    // ── Lookups ──────────────────────────────────────────────────

    public Task<bool> UserHasActiveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
        Context.ApplicationUserRoles.AnyAsync(ur => ur.UserId == userId && ur.Role.RoleName == roleName &&
            ur.User.AccountStatus == AccountStatus.Active && !ur.User.IsBanned, cancellationToken);

    public async Task<IReadOnlyDictionary<int, string>> GetTrainerRoleNamesAsync(CancellationToken cancellationToken = default) =>
        await Context.TrainerRoles.ToDictionaryAsync(t => t.Id, t => t.RoleName, cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveBannedWordsAsync(CancellationToken cancellationToken = default) =>
        await Context.BannedWordLists.Where(x => x.IsActive).Select(x => x.Word).ToListAsync(cancellationToken);

    public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
        Context.Gyms.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> IsAnyGymRecruitingAsync(CancellationToken cancellationToken = default) =>
        Context.Gyms.AnyAsync(gym => gym.IsAcceptingTrainerApplications, cancellationToken);

    public Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUsers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public Task<TrainerProfile?> GetProfileAsync(int id, CancellationToken cancellationToken = default) =>
        Context.TrainerProfiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<TrainerProfile?> GetProfileByUserAsync(string userId, bool activeOnly, CancellationToken cancellationToken = default) =>
        Context.TrainerProfiles.FirstOrDefaultAsync(x => x.PTUserId == userId && (!activeOnly || x.IsActive), cancellationToken);

    public Task<int?> GetActiveTrainerRoleIdAsync(string roleName, CancellationToken cancellationToken = default) =>
        Context.TrainerRoles.Where(t => t.RoleName == roleName && t.IsActive).Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TrainerRole?> GetTrainerRoleAsync(int? id, CancellationToken cancellationToken = default) =>
        Context.TrainerRoles.Include(t => t.Role).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    // ── Applications ─────────────────────────────────────────────

    public Task<TrainerApplication?> GetApplicationAsync(int id, CancellationToken cancellationToken = default) =>
        Context.TrainerApplications.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<TrainerApplication> Items, int TotalCount)> SearchApplicationsAsync(
        TrainerApplicationFilter filter, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var query = Context.TrainerApplications.AsQueryable();
        if (ownerUserId != null)
            query = query.Where(x => x.ApplicantUserId == ownerUserId || x.HiredUserId == ownerUserId);
        if (filter.ApplicationType != null)
            query = query.Where(x => x.ApplicationType == filter.ApplicationType);
        if (filter.Status != null)
        {
            var status = Enum.Parse<TrainerApplicationStatus>(filter.Status);
            query = query.Where(x => x.ApplicationStatus == status);
        }
        if (!string.IsNullOrEmpty(filter.TrainerRole))
            query = query.Where(x => x.TrainerRole!.RoleName == filter.TrainerRole);
        if (!string.IsNullOrWhiteSpace(filter.TrackingCode))
        {
            var trackingCode = filter.TrackingCode.Trim();
            query = query.Where(x => x.TrackingCode == trackingCode);
        }
        if (filter.From != null)
            query = query.Where(x => x.SubmittedAt >= filter.From);
        if (filter.To != null)
            query = query.Where(x => x.SubmittedAt <= filter.To);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<TrainerApplication>> GetApplicationsByUserAsync(string applicationType, string userId,
        CancellationToken cancellationToken = default) =>
        await Context.TrainerApplications.Where(x => x.ApplicationType == applicationType &&
            (x.ApplicantUserId == userId || x.HiredUserId == userId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainerApplication>> GetRecruitmentHistoryAsync(string email, string phoneNumber, int? applicationId, string? userId,
        CancellationToken cancellationToken = default)
    {
        email = UserIdentityRules.NormalizeEmail(email);
        var phones = TrainerApplicantRules.PhoneVariants(phoneNumber);
        var isGmail = email.EndsWith(GmailSuffix, StringComparison.Ordinal);
        var identity = email.Replace(".", "");
        return await Context.TrainerApplications.Where(x => x.ApplicationType == "Recruitment" &&
            (x.Id == applicationId || (userId != null && (x.ApplicantUserId == userId || x.HiredUserId == userId)) ||
             phones.Contains(x.PhoneNumber) || x.Email.ToLower() == email ||
             (isGmail && x.Email.ToLower().EndsWith(GmailSuffix) && x.Email.ToLower().Replace(".", "") == identity)))
            .ToListAsync(cancellationToken);
    }

    public Task<TrainerApplication?> GetApplicationByTrackingCodeAsync(string trackingCode, CancellationToken cancellationToken = default) =>
        Context.TrainerApplications.Include(x => x.TrainerRole)
            .FirstOrDefaultAsync(x => x.TrackingCode == trackingCode, cancellationToken);

    public Task<PhysicalProfile?> GetPhysicalProfileAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.PhysicalProfiles.FirstOrDefaultAsync(x => x.MemberUserId == userId, cancellationToken);

    public Task<TrainerApplication?> GetHiredRecruitmentAsync(int id, CancellationToken cancellationToken = default) =>
        Context.TrainerApplications.FirstOrDefaultAsync(x => x.Id == id && x.ApplicationType == "Recruitment" &&
            x.ApplicationStatus == TrainerApplicationStatus.Hired, cancellationToken);

    public async Task<IReadOnlyList<TrainerApplication>> GetApplicationsLinkedToUserAsync(ApplicationUser user,
        CancellationToken cancellationToken = default) =>
        await Context.TrainerApplications.Where(x => x.HiredUserId == user.UserId || x.ApplicantUserId == user.UserId ||
            x.Email.ToLower() == user.Email.ToLower()).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainerApplication>> GetExpiredSetupLinkApplicationsAsync(DateTime now, string notificationType,
        CancellationToken cancellationToken = default) =>
        await (from a in Context.TrainerApplications
               join u in Context.ApplicationUsers on a.HiredUserId equals u.UserId
               where a.ApplicationType == "Recruitment" && a.ApplicationStatus == TrainerApplicationStatus.Hired &&
                     u.AccountStatus == AccountStatus.PendingVerification && u.PasswordSetupExpiresAt <= now &&
                     !Context.Notifications.Any(n => n.NotificationType == notificationType &&
                         n.EntityType == nameof(TrainerApplication) && n.EntityId == a.Id && n.NotifiedAt >= u.PasswordSetupExpiresAt)
               select a).ToListAsync(cancellationToken);

    // ── Accounts ─────────────────────────────────────────────────

    // Reuse account contact normalization; dotted Gmail variants identify the same mailbox.
    private IQueryable<ApplicationUser> AccountsWithContacts(string email, string? phoneNumber)
    {
        email = UserIdentityRules.NormalizeEmail(email);
        var phones = TrainerApplicantRules.PhoneVariants(phoneNumber);
        var isGmail = email.EndsWith(GmailSuffix, StringComparison.Ordinal);
        var identity = email.Replace(".", "");
        return Context.ApplicationUsers.Where(user => user.Email.ToLower() == email || phones.Contains(user.PhoneNumber) ||
            (isGmail && user.Email.ToLower().EndsWith(GmailSuffix) && user.Email.ToLower().Replace(".", "") == identity));
    }

    public async Task<bool> IsStaffOrActiveTrainerAsync(string email, string phoneNumber, CancellationToken cancellationToken = default) =>
        await (from u in AccountsWithContacts(email, phoneNumber)
               join ur in Context.ApplicationUserRoles on u.UserId equals ur.UserId
               join r in Context.ApplicationRoles on ur.RoleId equals r.Id
               where StaffRoles.Contains(r.RoleName) ||
                   ((r.RoleName == "PT" || r.RoleName == YogaLoginRole) && u.AccountStatus == AccountStatus.Active)
               select u.Id).AnyAsync(cancellationToken) ||
        await (from p in Context.TrainerProfiles
               join u in AccountsWithContacts(email, phoneNumber) on p.PTUserId equals u.UserId
               where p.IsActive && u.AccountStatus == AccountStatus.Active
               select p.Id).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<ApplicationUser>> FindMatchingAccountsAsync(string email, string? phoneNumber,
        CancellationToken cancellationToken = default)
        => await AccountsWithContacts(email, phoneNumber).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetUsersWithAnyRoleAsync(IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default) =>
        await Context.ApplicationUserRoles.Where(ur => userIds.Contains(ur.UserId) && roleNames.Contains(ur.Role.RoleName))
            .Select(ur => ur.UserId).Distinct().ToListAsync(cancellationToken);

    public Task<bool> HasUnfinishedPTContractAsync(string userId, DateTime now, DateOnly makeUpFrom,
        CancellationToken cancellationToken = default) =>
        Context.PTPurchases.AnyAsync(p => p.MemberUserId == userId && !p.IsCompleted &&
            (p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt > now ||
             (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen) && p.EndDate >= makeUpFrom), cancellationToken);

    public Task<TrainerProfile?> FindProfileForHireAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.TrainerProfiles.FirstOrDefaultAsync(x => x.PTUserId == userId,
            cancellationToken);

    public Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.YogaClasses.AnyAsync(c => c.InstructorUserId == userId &&
            (c.ClassStatus == YogaClassStatus.Draft || c.ClassStatus == YogaClassStatus.Published), cancellationToken);

    public Task<bool> UserHasRoleIdAsync(string userId, int roleId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, cancellationToken);

    public async Task<IReadOnlyList<ApplicationUserRole>> GetRoleAssignmentsAsync(string userId, IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default) =>
        await Context.ApplicationUserRoles.Where(x => x.UserId == userId && roleNames.Contains(x.Role.RoleName))
            .ToListAsync(cancellationToken);

    public Task<int?> GetActiveLoginRoleIdAsync(int trainerRoleId, IReadOnlyCollection<string> loginRoles,
        CancellationToken cancellationToken = default) =>
        Context.TrainerRoles.Where(role => role.Id == trainerRoleId && role.IsActive && loginRoles.Contains(role.Role.RoleName))
            .Select(role => (int?)role.RoleId).FirstOrDefaultAsync(cancellationToken);

    public Task<ApplicationUser?> GetPendingTrainerAccountAsync(string userId, int loginRoleId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUsers.FirstOrDefaultAsync(x => x.UserId == userId &&
            x.AccountStatus == AccountStatus.PendingVerification && !x.IsBanned &&
            x.UserRoles.Any(userRole => userRole.RoleId == loginRoleId) &&
            !x.UserRoles.Any(userRole => userRole.RoleId != loginRoleId), cancellationToken);

    public Task<ApplicationUser?> FindEligiblePasswordSetupAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default) =>
        EligiblePasswordSetupUsers(now).AsNoTracking().FirstOrDefaultAsync(
            user => user.PasswordSetupTokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryCompletePasswordSetupAsync(string userId, string tokenHash, string passwordHash, DateTime now,
        CancellationToken cancellationToken = default) =>
        await EligiblePasswordSetupUsers(now)
            .Where(user => user.UserId == userId && user.PasswordSetupTokenHash == tokenHash)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.PasswordHash, passwordHash)
                .SetProperty(user => user.PasswordSetupTokenHash, (string?)null)
                .SetProperty(user => user.PasswordSetupExpiresAt, (DateTime?)null)
                .SetProperty(user => user.ResetTokenHash, (string?)null)
                .SetProperty(user => user.ResetTokenExpiresAt, (DateTime?)null)
                .SetProperty(user => user.AccountStatus, AccountStatus.Active)
                .SetProperty(user => user.UpdatedAt, now), cancellationToken) == 1;

    private IQueryable<ApplicationUser> EligiblePasswordSetupUsers(DateTime now) =>
        Context.ApplicationUsers.Where(user => user.AccountStatus == AccountStatus.PendingVerification &&
            !user.IsBanned && user.PasswordSetupExpiresAt > now &&
            Context.TrainerApplications.Any(application => application.HiredUserId == user.UserId &&
                application.ApplicationType == "Recruitment" && application.ApplicationStatus == TrainerApplicationStatus.Hired &&
                application.TrainerRole != null && application.TrainerRole.IsActive &&
                (application.TrainerRole.Role.RoleName == nameof(UserRole.PT) ||
                    application.TrainerRole.Role.RoleName == YogaLoginRole) &&
                user.UserRoles.Any(userRole => userRole.RoleId == application.TrainerRole.RoleId) &&
                !user.UserRoles.Any(userRole => userRole.RoleId != application.TrainerRole.RoleId)));

    // ── Public directory (UC 3.4) ────────────────────────────────

    // UC 3.4: the directory lists active PT and Yoga trainers; Yoga trainers teaching classes stay visible.
    private IQueryable<TrainerProfileResponse> PublicProfiles() =>
        from profile in Context.TrainerProfiles
        join user in Context.ApplicationUsers on profile.PTUserId equals user.UserId
        where profile.IsActive && user.AccountStatus == AccountStatus.Active && !user.IsBanned
        select new TrainerProfileResponse
        {
            Id = profile.Id,
            UserId = profile.PTUserId,
            FullName = user.FullName,
            Level = profile.Level.ToString(),
            Specialty = profile.Specialty,
            Gender = profile.Gender,
            ExperienceYears = profile.ExperienceYears,
            Bio = profile.Bio,
            AvatarUrl = profile.AvatarJson != null && profile.AvatarJson != ""
                ? "/api/trainers/" + profile.Id + "/avatar"
                : profile.AvatarUrl,
            // Written inline: EF cannot translate a helper method call inside this projection.
            AverageRating = Context.TrainerReviews
                .Where(r => r.TrainerUserId == profile.PTUserId && r.PTPurchaseId != null && r.IsVisible &&
                    r.ReviewStatus == TrainerReviewStatus.Approved)
                .Select(r => (decimal?)r.Rating).Average() ?? 0,
            IsAcceptingNewMembers = profile.AcceptingNewMembers,
            NotAcceptingSince = profile.NotAcceptingSince,
            NotAcceptingCountFrom = profile.NotAcceptingCountFrom ?? profile.NotAcceptingSince,
            NotAcceptingAccumulatedDays = profile.NotAcceptingAccumulatedDays,
            AcceptingSince = profile.AcceptingSince,
            TrainerRole = profile.TrainerRole != null ? profile.TrainerRole.RoleName : ""
        };

    // GBR-108: only PT contract reviews (UC 4.10) belong to a trainer; gym feedback has no PTPurchaseId.
    private IQueryable<TrainerReview> PublishedReviews(string trainerUserId) =>
        Context.TrainerReviews.Where(r => r.TrainerUserId == trainerUserId && r.PTPurchaseId != null && r.IsVisible &&
            r.ReviewStatus == TrainerReviewStatus.Approved);

    public async Task<(IReadOnlyList<TrainerProfileResponse> Items, int TotalCount)> SearchPublicProfilesAsync(TrainerSearch search,
        CancellationToken cancellationToken = default)
    {
        var query = PublicProfiles();
        if (search.Search != null)
            query = query.Where(x => x.FullName.Contains(search.Search) || x.Specialty.Contains(search.Search));
        if (!string.IsNullOrEmpty(search.TrainerRole))
            query = query.Where(x => x.TrainerRole == search.TrainerRole);
        if (search.Gender != null)
            query = query.Where(x => x.Gender == search.Gender);
        if (search.MinimumRating != null)
            query = query.Where(x => x.AverageRating >= search.MinimumRating);
        if (!string.IsNullOrEmpty(search.Level))
            query = query.Where(x => x.Level == search.Level);
        if (search.AcceptingNewMembers != null)
            query = query.Where(x => x.IsAcceptingNewMembers == search.AcceptingNewMembers);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<TrainerProfileResponse?> GetPublicProfileAsync(int id, CancellationToken cancellationToken = default) =>
        PublicProfiles().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> HasRunningContractWithTrainerAsync(string memberId, string trainerUserId, DateOnly today,
        CancellationToken cancellationToken = default) =>
        Context.PTPurchases.AnyAsync(x => x.MemberUserId == memberId && x.PTUserId == trainerUserId &&
            (x.Status == PTPurchaseStatus.Active || x.Status == PTPurchaseStatus.Frozen) &&
            !x.IsCompleted && x.EndDate >= today, cancellationToken);

    public Task<int> CountPublishedReviewsAsync(string trainerUserId, CancellationToken cancellationToken = default) =>
        PublishedReviews(trainerUserId).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainerReviewResponse>> GetPublishedReviewsAsync(string trainerUserId, int skip, int take,
        CancellationToken cancellationToken = default) =>
        await PublishedReviews(trainerUserId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take)
            .Select(x => new TrainerReviewResponse(x.Rating, x.Comment, x.MemberUser.FullName)).ToListAsync(cancellationToken);

    // ── Readiness (UC 4.12) ──────────────────────────────────────

    public Task<bool> HasRunningPTContractAsync(string trainerUserId, DateOnly today,
        CancellationToken cancellationToken = default) =>
        Context.PTPurchases.Where(p => p.PTUserId == trainerUserId).Where(PersonalTrainingRules.StillRunning(today))
            .AnyAsync(cancellationToken);

    // ── Files and email queue ────────────────────────────────────

    public async Task<TrainerStoredFile> SaveFileAsync(TrainerFileInput file, CancellationToken cancellationToken = default)
    {
        var contentType = TrainerFileRules.ContentType(file) ?? throw new InvalidOperationException("Invalid trainer file.");
        Directory.CreateDirectory(StorageRoot);
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(StorageRoot, key);
        pendingFiles.Add(path);
        await File.WriteAllBytesAsync(path, file.Content, cancellationToken);
        return new TrainerStoredFile { Id = key, StorageKey = key, FileName = file.FileName, ContentType = contentType };
    }

    public async Task<byte[]?> ReadFileAsync(TrainerStoredFile file, CancellationToken cancellationToken = default)
    {
        if (file.LegacyContentBase64 != null) return Convert.FromBase64String(file.LegacyContentBase64);
        if (!Guid.TryParseExact(file.StorageKey, "N", out _)) return null;
        var path = Path.Combine(StorageRoot, file.StorageKey);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    private static List<TrainerQueuedEmail> EmailQueue(TrainerApplication application) =>
        string.IsNullOrWhiteSpace(application.EmailQueueJson)
            ? []
            : JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(application.EmailQueueJson) ?? [];

    public void QueueEmail(TrainerApplication application, string recipient, string subject, string body)
    {
        var queue = EmailQueue(application);
        queue.Add(new TrainerQueuedEmail { Recipient = recipient, Subject = subject, ProtectedBody = EmailProtector.Protect(body) });
        application.EmailQueueJson = JsonSerializer.Serialize(queue);
    }

    public void RemoveUnsentEmails(TrainerApplication application, string subject)
    {
        var queue = EmailQueue(application);
        queue.RemoveAll(x => x.SentAt == null && x.Subject == subject);
        application.EmailQueueJson = JsonSerializer.Serialize(queue);
    }

    public void UpdateEmailQueue(TrainerApplication application, IReadOnlyList<TrainerQueuedEmail> queue) =>
        application.EmailQueueJson = JsonSerializer.Serialize(queue);

    public async Task RunMailDeliveryAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        // Same lock as recruitment writes: prevents overwriting newly queued emails or token reissues.
        await Context.Database.ExecuteSqlRawAsync(RecruitmentLock + "@LockTimeout=0; IF @r < 0 THROW 51001, 'Mail worker busy', 1;", cancellationToken);
        await work();
        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TrainerQueuedMail?> GetNextQueuedEmailAsync(DateTime now, string prioritySubject,
        CancellationToken cancellationToken = default)
    {
        var application = await Context.TrainerApplications.FromSqlInterpolated($"""
            SELECT TOP (1) a.*
            FROM TrainerApplications a
            CROSS APPLY (
                SELECT MIN(CASE WHEN e.Subject = {prioritySubject} THEN 0 ELSE 1 END) AS Priority
                FROM OPENJSON(a.EmailQueueJson) WITH (
                    Subject nvarchar(256) '$.Subject', SentAt datetime2 '$.SentAt',
                    NextAttemptAt datetime2 '$.NextAttemptAt', IsDeleted bit '$.IsDeleted'
                ) e
                WHERE ISNULL(e.IsDeleted,0)=0 AND e.SentAt IS NULL AND e.NextAttemptAt <= {now}
            ) ready
            WHERE a.IsDeleted=0 AND ready.Priority IS NOT NULL
            ORDER BY ready.Priority, a.Id
            """).FirstOrDefaultAsync(cancellationToken);
        if (application == null)
            return null;
        var queue = EmailQueue(application);
        var email = queue.Where(x => !x.IsDeleted && x.SentAt == null && x.NextAttemptAt <= now)
            .OrderByDescending(x => x.Subject == prioritySubject).FirstOrDefault();
        if (email == null)
            return null;
        string? body;
        try
        {
            body = EmailProtector.Unprotect(email.ProtectedBody);
        }
        catch (CryptographicException)
        {
            body = null; // The delivery step counts this as a failed attempt and retries later.
        }
        return new(application, queue, email, body);
    }
}
