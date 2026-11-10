using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class TrainerService(ITrainerStore store, IPasswordHasher passwords)
{
    private static Result<T> Error<T>(string code, string message, int status = 409) => Result<T>.Failure(message, code, status);
    private async Task<bool> Role(string? actor, string role, CancellationToken ct) => actor != null &&
        await store.Count(from u in store.Query<ApplicationUser>()
                          join ur in store.Query<ApplicationUserRole>() on u.UserId equals ur.UserId
                          join r in store.Query<ApplicationRole>() on ur.RoleId equals r.Id
                          where u.UserId == actor && u.AccountStatus == AccountStatus.Active && !u.IsBanned && r.RoleName == role
                          select u, ct) > 0;
    private async Task<bool> Moderated(string text, CancellationToken ct) =>
        (await store.List(store.Query<BannedWordList>().Where(x => x.IsActive).Select(x => x.Word), ct))
        .Any(w => !string.IsNullOrWhiteSpace(w) && text.Contains(w, StringComparison.OrdinalIgnoreCase));
    private void Audit(string? actor, string entity, int id, string action, string? before = null, string? after = null) => store.Add(new AuditLog
    {
        ActorUserId = actor,
        AuditEntity = entity,
        EntityId = id,
        ActionType = AuditActionType.Update,
        OldValue = before == null ? null : JsonSerializer.Serialize(new { Status = before }),
        NewValue = JsonSerializer.Serialize(new { Action = action, Status = after }),
        OccurredAt = DateTime.UtcNow
    });
    private static List<T> Records<T>(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];
    private void Mail(TrainerApplication application, string email, string subject, string body)
    {
        var queue = Records<TrainerQueuedEmail>(application.EmailQueueJson);
        queue.Add(store.Email(email, subject, body));
        application.EmailQueueJson = JsonSerializer.Serialize(queue);
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private async Task<string> SaveCertificates(IEnumerable<TrainerFileInput> files, CancellationToken ct)
    {
        var saved = new List<TrainerStoredFile>();
        foreach (var file in files)
        {
            var stored = await store.SaveFile(file, ct);
            stored.IsPublic = false;
            stored.IsAvatar = false;
            saved.Add(stored);
        }
        return JsonSerializer.Serialize(saved);
    }
    private static string PublishCertificates(string json)
    {
        var files = Records<TrainerStoredFile>(json);
        foreach (var file in files)
        {
            file.IsPublic = true;
            file.IsAvatar = false;
        }
        return JsonSerializer.Serialize(files);
    }
    private static IReadOnlyList<TrainerDocumentResponse> Docs(string json, string path, bool publicOnly = false) =>
        Records<TrainerStoredFile>(json).Where(d => !d.IsDeleted && !d.IsAvatar && (!publicOnly || d.IsPublic))
            .Select(d => new TrainerDocumentResponse(d.Id, d.FileName, d.ContentType, path + "/documents/" + Uri.EscapeDataString(d.Id))).ToList();

    public Task<Result<TrainerApplicationReceipt>> Submit(SubmitTrainerApplicationRequest r, string? actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!(await new SubmitTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid)
            return Error<TrainerApplicationReceipt>("TRAINER_VALIDATION", "Invalid application fields or certificates.", 400);
        var update = r.ApplicationType == "ProfileUpdate";
        var pt = await Role(actor, "PT", ct);
        if (update && !pt)
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "PT role required.", 403);
        if (!update && actor != null && !pt && !await Role(actor, "Customer", ct))
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Customer or PT role required.", 403);

        var email = r.Email.Trim().ToLowerInvariant();
        var citizen = r.CitizenId.Trim();
        var now = DateTime.UtcNow;
        if (update)
        {
            var owner = await store.First(store.Query<ApplicationUser>().Where(x => x.UserId == actor), ct);
            var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct);
            var original = await store.First(store.Query<TrainerApplication>().Where(x => x.HiredUserId == actor && x.ApplicationType == "Recruitment" && x.ApplicationStatus == TrainerApplicationStatus.Hired).OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id), ct);
            if (profile == null || owner == null || original == null || !string.Equals(owner.Email.Trim(), email, StringComparison.OrdinalIgnoreCase) || original.CitizenId != citizen)
                return Error<TrainerApplicationReceipt>("TRAINER_IDENTITY_CONFLICT", "Email must match the current account and citizen identity must match its hired application.");
        }
        if (pt && await store.Count(store.Query<TrainerApplication>().Where(x => (x.ApplicantUserId == actor || x.HiredUserId == actor || x.Email.ToLower() == email || x.CitizenId == citizen) && x.ApplicationType == r.ApplicationType && (x.ApplicationStatus == TrainerApplicationStatus.Pending || x.ApplicationStatus == TrainerApplicationStatus.UnderReview || x.ApplicationStatus == TrainerApplicationStatus.Interviewing)), ct) > 0)
            return Error<TrainerApplicationReceipt>("TRAINER_CHANGE_PENDING", "An application of this type is already pending.");
        var history = store.Query<TrainerApplication>().Where(x => x.ApplicationType == r.ApplicationType && (x.Email.ToLower() == email || x.CitizenId == citizen));
        var last = await store.First(history.OrderByDescending(x => x.SubmittedAt), ct);
        if (!update && last != null && last.SubmittedAt > now.AddDays(-30))
            return Error<TrainerApplicationReceipt>("RECRUITMENT_COOLDOWN", $"You can submit again after {last.SubmittedAt.AddDays(30):O}.");
        var a = new TrainerApplication
        {
            ApplicantUserId = actor,
            ApplicationType = r.ApplicationType,
            CertificatesJson = await SaveCertificates(r.Certificates, ct),
            FullName = r.FullName.Trim(),
            Email = email,
            PhoneNumber = r.PhoneNumber,
            CitizenId = citizen,
            DateOfBirth = r.DateOfBirth,
            Gender = r.Gender,
            Specialty = r.Specialty.Trim(),
            ExperienceYears = r.ExperienceYears,
            Bio = r.Bio.Trim(),
            ConsentAt = now,
            SubmittedAt = now,
            TrackingCode = "TR-" + Guid.NewGuid().ToString("N"),
            SubmissionAttempt = await store.Count(history, ct) + 1
        };
        store.Add(a);
        await store.Save(ct);
        Mail(a, email, "Trainer application received", $"Your tracking code is {a.TrackingCode}. Your application is Pending.");
        if (update)
        {
            var managers = await store.List((from ur in store.Query<ApplicationUserRole>() join role in store.Query<ApplicationRole>() on ur.RoleId equals role.Id where role.RoleName == "Manager" select ur.UserId).Distinct(), ct);
            foreach (var manager in managers) Notify(manager, actor!, a, "TrainerCredentialReview", "Trainer credentials pending review", "A trainer submitted credentials for review.");
        }
        Audit(actor, nameof(TrainerApplication), a.Id, "Submit", null, a.ApplicationStatus.ToString());
        return Result<TrainerApplicationReceipt>.Success(new(a.TrackingCode, "Pending", a.SubmissionAttempt), 201);
    }, ct);

    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> Applications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await Role(actor, "Manager", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        return await ApplicationList(r, store.Query<TrainerApplication>(), ct);
    }
    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> MyApplications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await Role(actor, "PT", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "PT role required.", 403);
        return await ApplicationList(r, store.Query<TrainerApplication>().Where(x => x.ApplicantUserId == actor || x.HiredUserId == actor), ct);
    }
    private async Task<Result<PagedResponse<TrainerApplicationResponse>>> ApplicationList(TrainerApplicationFilter r, IQueryable<TrainerApplication> q, CancellationToken ct)
    {
        if (!(await new TrainerApplicationFilterValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_VALIDATION", "Invalid filters.", 400);
        if (r.ApplicationType != null) q = q.Where(x => x.ApplicationType == r.ApplicationType);
        if (r.Status != null)
        {
            var status = Enum.Parse<TrainerApplicationStatus>(r.Status);
            q = q.Where(x => x.ApplicationStatus == status);
        }
        if (r.Specialty != null) q = q.Where(x => x.Specialty.Contains(r.Specialty));
        if (r.From != null)
            q = q.Where(x => x.SubmittedAt >= r.From);
        if (r.To != null)
            q = q.Where(x => x.SubmittedAt <= r.To);

        var total = await store.Count(q, ct);
        var items = await store.List(q.OrderByDescending(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize), ct);
        return Result<PagedResponse<TrainerApplicationResponse>>.Success(
            new(r.Page, r.PageSize, total, items.Select(ApplicationResponse).ToList()));
    }

    private static TrainerApplicationResponse ApplicationResponse(TrainerApplication a) =>
        new(a.Id, a.TrackingCode, a.ApplicationStatus.ToString(), a.FullName, a.Email, a.PhoneNumber,
            a.CitizenId, a.DateOfBirth, a.Gender, a.Specialty, a.ExperienceYears, a.Bio, a.SubmittedAt,
            a.SubmissionAttempt, a.ReviewNote, a.HiredUserId,
            Docs(a.CertificatesJson, $"/api/trainers/applications/{a.Id}"), a.ApplicationType);
    public async Task<Result<TrainerApplicationResponse>> Application(int id, string actor, CancellationToken ct)
    {
        if (!await Role(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        return a == null
            ? Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Application not found.", 404)
            : Result<TrainerApplicationResponse>.Success(ApplicationResponse(a));
    }

    public Task<Result<TrainerApplicationResponse>> Review(int id, ReviewTrainerApplicationRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        if (!(await new ReviewTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid) return Error<TrainerApplicationResponse>("TRAINER_VALIDATION", "Invalid decision or missing identity verification.", 400);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        if (a == null) return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Application not found.", 404);
        var before = a.ApplicationStatus.ToString();
        if (a.ApplicationType == "ProfileUpdate")
        {
            if (r.Action is not ("Approve" or "Reject") || a.ApplicationStatus is not (TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview))
                return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Pending profile updates can only be approved or rejected.");
            var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == a.ApplicantUserId && x.IsActive), ct);
            if (profile == null) return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Profile not found.", 404);
            if (r.Action == "Approve")
            {
                if (await Moderated(a.Specialty, ct)) return Error<TrainerApplicationResponse>("TRAINER_CONTENT_REJECTED", "Specialty contains prohibited content.", 400);
                profile.Specialty = a.Specialty;
                profile.CertificatesJson = PublishCertificates(a.CertificatesJson);
            }
            a.ApplicationStatus = r.Action == "Approve" ? TrainerApplicationStatus.Approved : TrainerApplicationStatus.Rejected;
            a.ReviewNote = r.Note.Trim();
            a.ReviewedByUserId = actor;
            a.ReviewedAt = DateTime.UtcNow;
            Notify(profile.PTUserId, actor, a, "TrainerCredentialDecision", "Trainer credentials reviewed", $"Your profile update was {a.ApplicationStatus.ToString().ToLowerInvariant()}.");
            Audit(actor, nameof(TrainerApplication), a.Id, r.Action, before, a.ApplicationStatus.ToString());
            return Result<TrainerApplicationResponse>.Success(ApplicationResponse(a));
        }
        if (r.Action == "Approve") return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Recruitment applications require Interview, Hire or Reject.");
        if (a.ApplicationStatus == TrainerApplicationStatus.Hired && r.Action == "Hire")
            return Result<TrainerApplicationResponse>.Success(ApplicationResponse(a));
        if (a.ApplicationStatus is not (TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview or TrainerApplicationStatus.Interviewing))
            return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Only pending/interviewing applications can be reviewed.");
        if (r.Action == "Hire")
        {
            if (a.ApplicationStatus is not (TrainerApplicationStatus.Interviewing or TrainerApplicationStatus.UnderReview)) return Error<TrainerApplicationResponse>("TRAINER_INTERVIEW_REQUIRED", "Record the interview before hiring.");
            var matches = await store.List(store.Query<ApplicationUser>().Where(x => x.Email.ToLower() == a.Email || x.PhoneNumber == a.PhoneNumber), ct);
            ApplicationUser? user = null;
            if (matches.Count > 0)
            {
                user = matches.SingleOrDefault(x => x.UserId == r.ExistingUserId);
                if (matches.Count != 1 || user == null || user.AccountStatus != AccountStatus.Active || user.IsBanned)
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Verify and explicitly select the existing active account; resolve conflicting identities first.");
            }
            else if (r.ExistingUserId != null) return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Selected account does not match applicant identity.");
            if (user != null && await store.Count(store.Query<TrainerProfile>().Where(x => x.PTUserId == user.UserId), ct) > 0) return Error<TrainerApplicationResponse>("TRAINER_PROFILE_EXISTS", "Account already has a trainer profile.");
            if (await Moderated(a.Bio + " " + a.Specialty, ct)) return Error<TrainerApplicationResponse>("TRAINER_CONTENT_REJECTED", "Applicant public content requires correction before hiring.", 400);
            var role = await store.First(store.Query<ApplicationRole>().Where(x => x.RoleName == "PT"), ct);
            var gym = await store.First(store.Query<Gym>().OrderBy(x => x.Id), ct);
            if (role == null || gym == null) return Error<TrainerApplicationResponse>("TRAINER_CONFIGURATION", "Gym/PT role is not configured.", 503);
            var isNew = user == null;
            if (isNew)
            {
                user = new ApplicationUser
                {
                    UserId = Guid.NewGuid().ToString(),
                    GymId = gym.Id,
                    FullName = a.FullName,
                    Email = a.Email,
                    PhoneNumber = a.PhoneNumber,
                    AccountStatus = AccountStatus.PendingVerification,
                    PasswordHash = passwords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
                };
                store.Add(user);
            }
            if (await store.Count(store.Query<ApplicationUserRole>().Where(x => x.UserId == user!.UserId && x.RoleId == role.Id), ct) == 0) store.Add(new ApplicationUserRole { UserId = user!.UserId, RoleId = role.Id });
            var profile = new TrainerProfile
            {
                PTUserId = user!.UserId,
                Bio = a.Bio,
                Specialty = a.Specialty,
                CertificatesJson = PublishCertificates(a.CertificatesJson),
                Gender = a.Gender,
                ExperienceYears = a.ExperienceYears,
                Level = Enum.Parse<TrainerLevel>(r.Level!),
                CommissionTier = r.CommissionTier!.Trim()
            };
            store.Add(profile);
            await store.Save(ct);
            a.HiredUserId = user.UserId;
            a.ApplicationStatus = TrainerApplicationStatus.Hired;
            if (isNew)
                QueueSetup(user, a);
            else
                Mail(a, user.Email, "Trainer role activated", "Your existing account now has PT access. Sign in again to refresh your roles.");
        }
        else
        {
            a.ApplicationStatus = r.Action == "Interview" ? TrainerApplicationStatus.Interviewing : TrainerApplicationStatus.Rejected;
            if (r.Action == "Reject") Mail(a, a.Email, "Trainer application decision", $"Application {a.TrackingCode} was rejected. Contact the gym for details.");
        }
        a.ReviewNote = r.Note.Trim();
        a.ReviewedByUserId = actor;
        a.ReviewedAt = DateTime.UtcNow;
        Audit(actor, nameof(TrainerApplication), a.Id, r.Action, before, a.ApplicationStatus.ToString());
        return Result<TrainerApplicationResponse>.Success(ApplicationResponse(a));
    }, ct);
    private void QueueSetup(ApplicationUser user, TrainerApplication application)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordSetupTokenHash = Hash(token);
        user.PasswordSetupExpiresAt = DateTime.UtcNow.AddHours(48);
        Mail(application, user.Email, "Set up your trainer account", "SETUP:" + token);
    }
    public Task<Result<bool>> ResendSetup(int applicationId, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == applicationId && x.ApplicationStatus == TrainerApplicationStatus.Hired), ct);
        var u = a == null ? null : await store.First(store.Query<ApplicationUser>().Where(x => x.UserId == a.HiredUserId), ct);
        if (u == null || u.AccountStatus != AccountStatus.PendingVerification) return Error<bool>("TRAINER_INVALID_STATE", "Only pending hired accounts can receive a setup link.");
        await ClearSetupEmails(u, ct);
        QueueSetup(u, a!);
        var status = a!.ApplicationStatus.ToString();
        Audit(actor, nameof(TrainerApplication), applicationId, "ResendSetup", status, status);
        return Result<bool>.Success(true);
    }, ct);
    public Task<Result<bool>> Setup(TrainerSetupRequest r, CancellationToken ct) => store.Write(async () =>
    {
        if (!(await new TrainerSetupValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid token/password format.", 400);
        var hash = Hash(r.Token);
        var now = DateTime.UtcNow;
        var u = await store.First(store.Query<ApplicationUser>().Where(x => x.PasswordSetupTokenHash == hash && x.PasswordSetupExpiresAt > now), ct);
        if (u == null || u.AccountStatus != AccountStatus.PendingVerification || u.IsBanned) return Error<bool>("TRAINER_SETUP_INVALID", "Setup link is invalid or expired.", 400);
        u.PasswordHash = passwords.Hash(r.Password);
        u.AccountStatus = AccountStatus.Active;
        u.PasswordSetupTokenHash = null;
        u.PasswordSetupExpiresAt = null;
        await ClearSetupEmails(u, ct);
        Audit(u.UserId, nameof(ApplicationUser), u.Id, "TrainerPasswordSetup",
            nameof(AccountStatus.PendingVerification), nameof(AccountStatus.Active));
        return Result<bool>.Success(true);
    }, ct);
    private IQueryable<TrainerProfileResponse> PublicProfiles() => from p in store.Query<TrainerProfile>()
                                                                   join u in store.Query<ApplicationUser>() on p.PTUserId equals u.UserId
                                                                   where p.IsActive && u.AccountStatus == AccountStatus.Active && !u.IsBanned
                                                                   select new TrainerProfileResponse
                                                                   {
                                                                       Id = p.Id,
                                                                       UserId = p.PTUserId,
                                                                       FullName = u.FullName,
                                                                       Level = p.Level.ToString(),
                                                                       Specialty = p.Specialty,
                                                                       Gender = p.Gender,
                                                                       ExperienceYears = p.ExperienceYears,
                                                                       Bio = p.Bio,
                                                                       AvatarUrl = p.AvatarJson != null && p.AvatarJson != "" ? "/api/trainers/" + p.Id + "/avatar" : p.AvatarUrl,
                                                                       AverageRating = store.Query<TrainerReview>().Where(r => r.TrainerUserId == p.PTUserId && r.IsVisible && r.ReviewStatus == TrainerReviewStatus.Approved).Select(r => (decimal?)r.Rating).Average() ?? 0
                                                                   };
    public async Task<Result<PagedResponse<TrainerProfileResponse>>> Search(TrainerSearch r, CancellationToken ct)
    {
        if (!(await new TrainerSearchValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerProfileResponse>>("TRAINER_VALIDATION", "Invalid filters.", 400);
        var q = PublicProfiles();
        if (r.Search != null)
            q = q.Where(x => x.FullName.Contains(r.Search));
        if (r.Specialty != null)
            q = q.Where(x => x.Specialty.Contains(r.Specialty));
        if (r.Gender != null)
            q = q.Where(x => x.Gender == r.Gender);
        if (r.MinimumRating != null)
            q = q.Where(x => x.AverageRating >= r.MinimumRating);
        return Result<PagedResponse<TrainerProfileResponse>>.Success(new(r.Page, r.PageSize, await store.Count(q, ct), await store.List(q.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize), ct)));
    }
    public async Task<Result<TrainerPublicDetail>> Public(int id, CancellationToken ct)
    {
        var p = await store.First(PublicProfiles().Where(x => x.Id == id), ct);
        if (p == null)
            return Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Trainer not found.", 404);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var end = today.AddDays(14);
        var stored = await store.First(store.Query<TrainerProfile>().Where(x => x.Id == id), ct);
        var certificates = Docs(stored!.CertificatesJson, $"/api/trainers/{id}", true);
        if (Records<TrainerStoredFile>(stored.CertificatesJson).Count == 0 && !string.IsNullOrWhiteSpace(stored.CertificateUrl))
            certificates = [new TrainerDocumentResponse("legacy", "Certificate", "application/octet-stream", stored.CertificateUrl)];
        var slots = await store.List(store.Query<PTAvailability>().Where(x => x.PTUserId == p.UserId &&
            (x.DayOfWeek != null && x.SlotStatus == SlotStatus.Available || x.AvailableDate >= today && x.AvailableDate < end)), ct);
        var now = DateTime.UtcNow;
        var booked = await store.List(store.Query<PTSession>()
            .Where(x => x.PTUserId == p.UserId && x.SessionDate >= today && x.SessionDate < end)
            .Where(PersonalTrainingRules.OccupiesTime(now)), ct);
        var availability = PersonalTrainingRules.Occurrences(today, end.AddDays(-1), slots, int.MaxValue)
            .Concat(slots.Where(x => x.AvailableDate != null).Select(x => (Date: x.AvailableDate!.Value, Slot: x)))
            .OrderBy(x => x.Date).ThenBy(x => x.Slot.StartTime)
            .Select(x => new TrainerAvailabilityResponse(x.Date, x.Slot.StartTime, x.Slot.EndTime,
                booked.Any(b => b.SessionDate == x.Date && PersonalTrainingRules.Overlaps(x.Slot.StartTime, x.Slot.EndTime, b.StartTime, b.EndTime))
                    ? nameof(SlotStatus.Booked) : x.Slot.SlotStatus.ToString())).ToList();
        return Result<TrainerPublicDetail>.Success(new(p, certificates, availability,
            await store.List(store.Query<TrainerReview>().Where(x => x.TrainerUserId == p.UserId && x.IsVisible && x.ReviewStatus == TrainerReviewStatus.Approved).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new TrainerReviewResponse(x.Rating, x.Comment)), ct)));
    }
    public async Task<Result<TrainerPublicDetail>> Mine(string actor, CancellationToken ct)
    {
        if (!await Role(actor, "PT", ct)) return Error<TrainerPublicDetail>("TRAINER_FORBIDDEN", "PT role required.", 403);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor), ct);
        return p == null
            ? Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Profile not found.", 404)
            : await Public(p.Id, ct);
    }
    public Task<Result<bool>> Update(UpdateTrainerProfileRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "PT", ct)) return Error<bool>("TRAINER_FORBIDDEN", "PT role required.", 403);
        if (!(await new TrainerProfileValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid bio or avatar.", 400);
        if (await Moderated(r.Bio, ct)) return Error<bool>("TRAINER_CONTENT_REJECTED", "Biography contains prohibited content.", 400);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct);
        if (p == null)
            return Error<bool>("TRAINER_NOT_FOUND", "Profile not found.", 404);

        p.Bio = r.Bio.Trim();
        if (r.Avatar != null)
        {
            var d = await store.SaveFile(r.Avatar, ct);
            d.IsAvatar = true;
            d.IsPublic = true;
            p.AvatarJson = JsonSerializer.Serialize(d);
        }
        Audit(actor, nameof(TrainerProfile), p.Id, "UpdatePublicProfile", "Active", "Active");
        return Result<bool>.Success(true);
    }, ct);
    private async Task ClearSetupEmails(ApplicationUser user, CancellationToken ct)
    {
        var applications = await store.List(store.Query<TrainerApplication>().Where(x => x.HiredUserId == user.UserId || x.ApplicantUserId == user.UserId || x.Email.ToLower() == user.Email.ToLower()), ct);
        foreach (var application in applications)
        {
            var queue = Records<TrainerQueuedEmail>(application.EmailQueueJson);
            queue.RemoveAll(x => x.SentAt == null && x.Subject == "Set up your trainer account");
            application.EmailQueueJson = JsonSerializer.Serialize(queue);
        }
    }
    private void Notify(string recipient, string actor, TrainerApplication application, string type, string title, string message) =>
        store.Add(new Notification { RecipientUserId = recipient, SenderUserId = actor, NotificationType = type, Title = title, Message = message, EntityType = nameof(TrainerApplication), EntityId = application.Id, NotifiedAt = DateTime.UtcNow });

    private async Task<bool> OwnsApplication(TrainerApplication application, string? actor, CancellationToken ct) =>
        actor != null && (application.ApplicantUserId == actor || application.HiredUserId == actor) && await Role(actor, "PT", ct);

    public async Task<Result<TrainerFileResponse>> ApplicationDocument(int id, string fileId, string? actor, CancellationToken ct)
    {
        var application = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        if (application == null || (!await Role(actor, "Manager", ct) && !await OwnsApplication(application, actor, ct)))
            return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        return await ReadDocument(Records<TrainerStoredFile>(application.CertificatesJson).FirstOrDefault(x => x.Id == fileId && !x.IsAvatar), ct);
    }
    public async Task<Result<TrainerFileResponse>> Document(int id, string fileId, string? actor, CancellationToken ct) =>
        await ProfileFile(id, fileId, false, actor, ct);

    public async Task<Result<TrainerFileResponse>> Avatar(int id, string? actor, CancellationToken ct) =>
        await ProfileFile(id, null, true, actor, ct);

    private async Task<Result<TrainerFileResponse>> ProfileFile(int id, string? fileId, bool avatar, string? actor, CancellationToken ct)
    {
        var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.Id == id), ct);
        if (profile == null) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var file = avatar
            ? (string.IsNullOrWhiteSpace(profile.AvatarJson) ? null : JsonSerializer.Deserialize<TrainerStoredFile>(profile.AvatarJson))
            : Records<TrainerStoredFile>(profile.CertificatesJson).FirstOrDefault(x => x.Id == fileId && !x.IsAvatar);
        if (file == null || file.IsDeleted) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var allowed = file.IsPublic && await store.Count(PublicProfiles().Where(x => x.Id == id), ct) > 0;
        if (!allowed) allowed = await Role(actor, "Manager", ct) || (actor == profile.PTUserId && await Role(actor, "PT", ct));
        return allowed ? await ReadDocument(file, ct) : Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
    }
    private async Task<Result<TrainerFileResponse>> ReadDocument(TrainerStoredFile? file, CancellationToken ct)
    {
        if (file == null || file.IsDeleted) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var content = await store.ReadFile(file, ct);
        return content == null ? Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404)
            : Result<TrainerFileResponse>.Success(new(file.FileName, file.ContentType, content));
    }
}
