using System.Security.Cryptography;
using System.Text;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class TrainerService(ITrainerStore store, IPasswordHasher passwords)
{
    private static Result<T> Error<T>(string code, string message, int status = 409) => Result<T>.Failure(message, code, status);
    private Task<bool> Role(string? actor, string role, CancellationToken ct) => HasRole(actor, role, ct);
    private async Task<bool> HasRole(string? actor, string role, CancellationToken ct) => actor != null &&
        await store.Count(from u in store.Query<ApplicationUser>()
                          join ur in store.Query<ApplicationUserRole>() on u.UserId equals ur.UserId
                          join r in store.Query<ApplicationRole>() on ur.RoleId equals r.Id
                          where u.UserId == actor && u.AccountStatus == AccountStatus.Active && !u.IsBanned && r.RoleName == role
                          select u, ct) > 0;
    private async Task<bool> Moderated(string text, CancellationToken ct) =>
        (await store.List(store.Query<BannedWordList>().Where(x => x.IsActive).Select(x => x.Word), ct))
        .Any(w => !string.IsNullOrWhiteSpace(w) && text.Contains(w, StringComparison.OrdinalIgnoreCase));
    private void Audit(string actor, string entity, int id, string action) => store.Add(new AuditLog
    {
        ActorUserId = actor,
        AuditEntity = entity,
        EntityId = id,
        ActionType = AuditActionType.Update,
        NewValue = action,
        OccurredAt = DateTime.UtcNow
    });
    private void Mail(string email, string subject, string body) => store.Add(store.Email(email, subject, body));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static TrainerDocument File(TrainerFileInput f) => new()
    {
        FileName = f.FileName,
        Content = f.Content,
        ContentType = TrainerFileRules.ContentType(f)!
    };
    private IQueryable<TrainerDocumentResponse> Docs(IQueryable<TrainerDocument> query) => query.Select(d => new TrainerDocumentResponse(d.Id, d.FileName, d.ContentType));

    public Task<Result<TrainerApplicationReceipt>> Submit(SubmitTrainerApplicationRequest r, string? actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!(await new SubmitTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid)
            return Error<TrainerApplicationReceipt>("TRAINER_VALIDATION", "Invalid application fields or certificates.", 400);
        if (actor != null && !await Role(actor, "Customer", ct)) return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Customer role required.", 403);
        var email = r.Email.Trim().ToLowerInvariant(); var citizen = r.CitizenId.Trim(); var now = DateTime.UtcNow;
        if (await store.Count(store.Query<TrainerRecruitmentRestriction>().Where(x => x.IsActive && (x.Email == email || x.CitizenId == citizen)), ct) > 0)
            return Error<TrainerApplicationReceipt>("RECRUITMENT_RESTRICTED", "Recruitment submission is restricted.", 403);
        var history = store.Query<TrainerApplication>().Where(x => x.Email.ToLower() == email || x.CitizenId == citizen);
        var last = await store.First(history.OrderByDescending(x => x.SubmittedAt), ct);
        if (last != null && last.SubmittedAt > now.AddDays(-30))
            return Error<TrainerApplicationReceipt>("RECRUITMENT_COOLDOWN", $"You can submit again after {last.SubmittedAt.AddDays(30):O}.");
        var a = new TrainerApplication
        {
            ApplicantUserId = actor,
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
        store.Add(a); await store.Save(ct);
        foreach (var f in r.Certificates) { var d = File(f); d.TrainerApplicationId = a.Id; store.Add(d); }
        Mail(email, "Trainer application received", $"Your tracking code is {a.TrackingCode}. Your application is Pending Review.");
        return Result<TrainerApplicationReceipt>.Success(new(a.TrackingCode, "Pending", a.SubmissionAttempt), 201);
    }, ct);

    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> Applications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await Role(actor, "Manager", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        if (!(await new TrainerApplicationFilterValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_VALIDATION", "Invalid filters.", 400);
        var q = store.Query<TrainerApplication>();
        if (r.Status != null) { var status = Enum.Parse<TrainerApplicationStatus>(r.Status); q = q.Where(x => x.ApplicationStatus == status); }
        if (r.Specialty != null) q = q.Where(x => x.Specialty.Contains(r.Specialty));
        if (r.From != null) q = q.Where(x => x.SubmittedAt >= r.From); if (r.To != null) q = q.Where(x => x.SubmittedAt <= r.To);
        var total = await store.Count(q, ct); var items = await store.List(q.OrderByDescending(x => x.SubmittedAt).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize), ct);
        var list = new List<TrainerApplicationResponse>(); foreach (var a in items) list.Add(await ApplicationResponse(a, ct));
        return Result<PagedResponse<TrainerApplicationResponse>>.Success(new(r.Page, r.PageSize, total, list));
    }
    private async Task<TrainerApplicationResponse> ApplicationResponse(TrainerApplication a, CancellationToken ct) =>
        new(a.Id, a.TrackingCode, a.ApplicationStatus.ToString(), a.FullName, a.Email, a.PhoneNumber, a.CitizenId, a.DateOfBirth, a.Gender, a.Specialty, a.ExperienceYears, a.Bio, a.SubmittedAt, a.SubmissionAttempt, a.ReviewNote, a.HiredUserId,
            await store.List(Docs(store.Query<TrainerDocument>().Where(d => d.TrainerApplicationId == a.Id)), ct));
    public async Task<Result<TrainerApplicationResponse>> Application(int id, string actor, CancellationToken ct)
    {
        if (!await Role(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        return a == null ? Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Application not found.", 404) : Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct));
    }

    public Task<Result<TrainerApplicationResponse>> Review(int id, ReviewTrainerApplicationRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        if (!(await new ReviewTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid) return Error<TrainerApplicationResponse>("TRAINER_VALIDATION", "Invalid decision or missing identity verification.", 400);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        if (a == null) return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Application not found.", 404);
        if (a.ApplicationStatus == TrainerApplicationStatus.Hired && r.Action == "Hire") return Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct));
        if (a.ApplicationStatus is not (TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview or TrainerApplicationStatus.Interviewing))
            return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Only pending/interviewing applications can be reviewed.");
        if (r.Action == "Hire")
        {
            if (a.ApplicationStatus is not (TrainerApplicationStatus.Interviewing or TrainerApplicationStatus.UnderReview)) return Error<TrainerApplicationResponse>("TRAINER_INTERVIEW_REQUIRED", "Record the interview before hiring.");
            if (await store.Count(store.Query<TrainerRecruitmentRestriction>().Where(x => x.IsActive && (x.Email == a.Email || x.CitizenId == a.CitizenId)), ct) > 0) return Error<TrainerApplicationResponse>("RECRUITMENT_RESTRICTED", "Resolve recruitment restriction before hiring.");
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
                }; store.Add(user);
            }
            if (await store.Count(store.Query<ApplicationUserRole>().Where(x => x.UserId == user!.UserId && x.RoleId == role.Id), ct) == 0) store.Add(new ApplicationUserRole { UserId = user!.UserId, RoleId = role.Id });
            var profile = new TrainerProfile
            {
                PTUserId = user!.UserId,
                Bio = a.Bio,
                Specialty = a.Specialty,
                Gender = a.Gender,
                ExperienceYears = a.ExperienceYears,
                Level = Enum.Parse<TrainerLevel>(r.Level!),
                CommissionTier = r.CommissionTier!.Trim()
            }; store.Add(profile); await store.Save(ct);
            foreach (var d in await store.List(store.Query<TrainerDocument>().Where(x => x.TrainerApplicationId == a.Id), ct))
            {
                // Copy approved credentials, keeping the original application attachment private.
                store.Add(new TrainerDocument { TrainerProfileId = profile.Id, FileName = d.FileName, ContentType = d.ContentType, Content = d.Content, IsPublic = true });
            }
            a.HiredUserId = user.UserId; a.ApplicationStatus = TrainerApplicationStatus.Hired;
            if (isNew) QueueSetup(user); else Mail(user.Email, "Trainer role activated", "Your existing account now has PT access. Sign in again to refresh your roles.");
        }
        else
        {
            a.ApplicationStatus = r.Action == "Interview" ? TrainerApplicationStatus.Interviewing : TrainerApplicationStatus.Rejected;
            if (r.Action == "Reject") Mail(a.Email, "Trainer application decision", $"Application {a.TrackingCode} was rejected. Contact the gym for details.");
        }
        a.ReviewNote = r.Note.Trim(); a.ReviewedByUserId = actor; a.ReviewedAt = DateTime.UtcNow;
        Audit(actor, nameof(TrainerApplication), a.Id, r.Action);
        return Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct));
    }, ct);
    private void QueueSetup(ApplicationUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        store.Add(new TrainerSetupToken { UserId = user.UserId, TokenHash = Hash(token), ExpiresAt = DateTime.UtcNow.AddHours(24) });
        Mail(user.Email, "Set up your trainer account", "SETUP:" + token);
    }
    public Task<Result<bool>> ResendSetup(int applicationId, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == applicationId && x.ApplicationStatus == TrainerApplicationStatus.Hired), ct);
        var u = a == null ? null : await store.First(store.Query<ApplicationUser>().Where(x => x.UserId == a.HiredUserId), ct);
        if (u == null || u.AccountStatus != AccountStatus.PendingVerification) return Error<bool>("TRAINER_INVALID_STATE", "Only pending hired accounts can receive a setup link.");
        foreach (var t in await store.List(store.Query<TrainerSetupToken>().Where(x => x.UserId == u.UserId && x.UsedAt == null), ct)) t.UsedAt = DateTime.UtcNow;
        QueueSetup(u); Audit(actor, nameof(TrainerApplication), applicationId, "ResendSetup"); return Result<bool>.Success(true);
    }, ct);
    public Task<Result<bool>> Setup(TrainerSetupRequest r, CancellationToken ct) => store.Write(async () =>
    {
        if (!(await new TrainerSetupValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid token/password format.", 400);
        var hash = Hash(r.Token); var now = DateTime.UtcNow;
        var t = await store.First(store.Query<TrainerSetupToken>().Where(x => x.TokenHash == hash && x.UsedAt == null && x.ExpiresAt > now), ct);
        var u = t == null ? null : await store.First(store.Query<ApplicationUser>().Where(x => x.UserId == t.UserId), ct);
        if (t == null || u == null || u.AccountStatus != AccountStatus.PendingVerification || u.IsBanned) return Error<bool>("TRAINER_SETUP_INVALID", "Setup link is invalid or expired.", 400);
        u.PasswordHash = passwords.Hash(r.Password); u.AccountStatus = AccountStatus.Active; t.UsedAt = now;
        Audit(u.UserId, nameof(ApplicationUser), u.Id, "TrainerPasswordSetup"); return Result<bool>.Success(true);
    }, ct);
    public Task<Result<bool>> Restrict(RecruitmentRestrictionRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        if (!(await new RestrictionValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid restriction fields.", 400);
        var email = r.Email.Trim().ToLowerInvariant(); var rows = await store.List(store.Query<TrainerRecruitmentRestriction>().Where(x => x.Email == email || x.CitizenId == r.CitizenId), ct);
        if (rows.Any(x => x.Email != email || x.CitizenId != r.CitizenId)) return Error<bool>("TRAINER_IDENTITY_CONFLICT", "Restriction identity differs; resolve the recorded identity first.");
        var item = rows.FirstOrDefault(); if (item == null) { item = new() { Email = email, CitizenId = r.CitizenId }; store.Add(item); }
        item.IsActive = r.IsActive; item.Reason = r.Reason; item.ChangedByUserId = actor; await store.Save(ct); Audit(actor, nameof(TrainerRecruitmentRestriction), item.Id, r.IsActive ? "Block" : "Unblock"); return Result<bool>.Success(true);
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
                                                                       AvatarFileId = p.AvatarFileId,
                                                                       AverageRating = store.Query<TrainerReview>().Where(r => r.TrainerUserId == p.PTUserId && r.IsVisible && r.ReviewStatus == TrainerReviewStatus.Approved).Select(r => (decimal?)r.Rating).Average() ?? 0
                                                                   };
    public async Task<Result<PagedResponse<TrainerProfileResponse>>> Search(TrainerSearch r, CancellationToken ct)
    {
        if (!(await new TrainerSearchValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerProfileResponse>>("TRAINER_VALIDATION", "Invalid filters.", 400);
        var q = PublicProfiles(); if (r.Search != null) q = q.Where(x => x.FullName.Contains(r.Search)); if (r.Specialty != null) q = q.Where(x => x.Specialty.Contains(r.Specialty)); if (r.Gender != null) q = q.Where(x => x.Gender == r.Gender); if (r.MinimumRating != null) q = q.Where(x => x.AverageRating >= r.MinimumRating);
        return Result<PagedResponse<TrainerProfileResponse>>.Success(new(r.Page, r.PageSize, await store.Count(q, ct), await store.List(q.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize), ct)));
    }
    public async Task<Result<TrainerPublicDetail>> Public(int id, CancellationToken ct)
    {
        var p = await store.First(PublicProfiles().Where(x => x.Id == id), ct); if (p == null) return Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Trainer not found.", 404);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var end = today.AddDays(14);
        return Result<TrainerPublicDetail>.Success(new(p, await store.List(Docs(store.Query<TrainerDocument>().Where(x => x.TrainerProfileId == id && x.IsPublic && !x.IsAvatar)), ct),
            await store.List(store.Query<PTAvailability>().Where(x => x.PTUserId == p.UserId && x.AvailableDate >= today && x.AvailableDate < end).OrderBy(x => x.AvailableDate).ThenBy(x => x.StartTime).Select(x => new TrainerAvailabilityResponse(x.AvailableDate, x.StartTime, x.EndTime, x.SlotStatus.ToString())), ct),
            await store.List(store.Query<TrainerReview>().Where(x => x.TrainerUserId == p.UserId && x.IsVisible && x.ReviewStatus == TrainerReviewStatus.Approved).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new TrainerReviewResponse(x.Rating, x.Comment)), ct)));
    }
    public async Task<Result<TrainerPublicDetail>> Mine(string actor, CancellationToken ct)
    {
        if (!await Role(actor, "PT", ct)) return Error<TrainerPublicDetail>("TRAINER_FORBIDDEN", "PT role required.", 403);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor), ct); return p == null ? Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Profile not found.", 404) : await Public(p.Id, ct);
    }
    public Task<Result<bool>> Update(UpdateTrainerProfileRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "PT", ct)) return Error<bool>("TRAINER_FORBIDDEN", "PT role required.", 403);
        if (!(await new TrainerProfileValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid bio or avatar.", 400);
        if (await Moderated(r.Bio, ct)) return Error<bool>("TRAINER_CONTENT_REJECTED", "Biography contains prohibited content.", 400);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct); if (p == null) return Error<bool>("TRAINER_NOT_FOUND", "Profile not found.", 404);
        p.Bio = r.Bio.Trim(); if (r.Avatar != null)
        {
            foreach (var old in await store.List(store.Query<TrainerDocument>().Where(x => x.TrainerProfileId == p.Id && x.IsAvatar), ct)) old.IsPublic = false;
            var d = File(r.Avatar); d.TrainerProfileId = p.Id; d.IsAvatar = true; d.IsPublic = true; store.Add(d); await store.Save(ct); p.AvatarFileId = d.Id;
        }
        Audit(actor, nameof(TrainerProfile), p.Id, "UpdatePublicProfile"); return Result<bool>.Success(true);
    }, ct);
    public Task<Result<int>> RequestCredentials(TrainerCredentialRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "PT", ct)) return Error<int>("TRAINER_FORBIDDEN", "PT role required.", 403);
        if (!(await new CredentialValidator().ValidateAsync(r, ct)).IsValid || await Moderated(r.Specialty, ct)) return Error<int>("TRAINER_VALIDATION", "Invalid specialty or certificates.", 400);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct); if (p == null) return Error<int>("TRAINER_NOT_FOUND", "Profile not found.", 404);
        if (await store.Count(store.Query<TrainerCredentialChange>().Where(x => x.TrainerProfileId == p.Id && x.Status == "Pending"), ct) > 0) return Error<int>("TRAINER_CHANGE_PENDING", "A credential change is already pending.");
        var change = new TrainerCredentialChange { TrainerProfileId = p.Id, Specialty = r.Specialty.Trim() }; store.Add(change); await store.Save(ct);
        foreach (var f in r.Certificates) { var d = File(f); d.TrainerProfileId = p.Id; d.TrainerCredentialChangeId = change.Id; store.Add(d); }
        var managers = await store.List((from ur in store.Query<ApplicationUserRole>() join role in store.Query<ApplicationRole>() on ur.RoleId equals role.Id where role.RoleName == "Manager" select ur.UserId).Distinct(), ct);
        foreach (var m in managers) store.Add(new Notification { RecipientUserId = m, SenderUserId = actor, NotificationType = "TrainerCredentialReview", Title = "Trainer credentials pending review", Message = "A trainer submitted credentials for review.", EntityType = nameof(TrainerCredentialChange), EntityId = change.Id, NotifiedAt = DateTime.UtcNow });
        Audit(actor, nameof(TrainerCredentialChange), change.Id, "Submit"); return Result<int>.Success(change.Id, 201);
    }, ct);
    public async Task<Result<PagedResponse<TrainerCredentialResponse>>> Changes(string actor, int page, int size, CancellationToken ct)
    {
        if (page < 1 || page > 10000 || size < 1 || size > 100) return Error<PagedResponse<TrainerCredentialResponse>>("TRAINER_VALIDATION", "Invalid pagination.", 400);
        var manager = await Role(actor, "Manager", ct); if (!manager && !await Role(actor, "PT", ct)) return Error<PagedResponse<TrainerCredentialResponse>>("TRAINER_FORBIDDEN", "PT or Manager role required.", 403);
        var q = store.Query<TrainerCredentialChange>(); if (!manager) q = q.Where(x => store.Query<TrainerProfile>().Any(p => p.Id == x.TrainerProfileId && p.PTUserId == actor));
        var total = await store.Count(q, ct); var result = new List<TrainerCredentialResponse>();
        foreach (var c in await store.List(q.OrderByDescending(x => x.Id).Skip((page - 1) * size).Take(size), ct)) result.Add(new(c.Id, c.TrainerProfileId, c.Specialty, c.Status, c.ReviewNote, c.CreatedAt, await store.List(Docs(store.Query<TrainerDocument>().Where(x => x.TrainerCredentialChangeId == c.Id)), ct)));
        return Result<PagedResponse<TrainerCredentialResponse>>.Success(new(page, size, total, result));
    }
    public Task<Result<bool>> ReviewCredentials(int id, ReviewTrainerCredentialRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await Role(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        if (!(await new CredentialReviewValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Review note required.", 400);
        var c = await store.First(store.Query<TrainerCredentialChange>().Where(x => x.Id == id), ct); if (c == null) return Error<bool>("TRAINER_NOT_FOUND", "Change not found.", 404);
        if (c.Status != "Pending") return Error<bool>("TRAINER_INVALID_STATE", "Change was already reviewed.");
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.Id == c.TrainerProfileId), ct); if (p == null) return Error<bool>("TRAINER_NOT_FOUND", "Profile not found.", 404);
        if (r.Approve)
        {
            if (await Moderated(c.Specialty, ct)) return Error<bool>("TRAINER_CONTENT_REJECTED", "Specialty contains prohibited content.", 400);
            p.Specialty = c.Specialty; foreach (var d in await store.List(store.Query<TrainerDocument>().Where(x => x.TrainerProfileId == p.Id && !x.IsAvatar && x.IsPublic), ct)) d.IsPublic = false;
            foreach (var d in await store.List(store.Query<TrainerDocument>().Where(x => x.TrainerCredentialChangeId == id), ct)) d.IsPublic = true;
        }
        c.Status = r.Approve ? "Approved" : "Rejected"; c.ReviewNote = r.Note; c.ReviewedByUserId = actor; c.ReviewedAt = DateTime.UtcNow;
        store.Add(new Notification { RecipientUserId = p.PTUserId, SenderUserId = actor, NotificationType = "TrainerCredentialDecision", Title = "Trainer credentials reviewed", Message = $"Your credential change was {c.Status.ToLowerInvariant()}.", EntityType = nameof(TrainerCredentialChange), EntityId = c.Id, NotifiedAt = DateTime.UtcNow });
        Audit(actor, nameof(TrainerCredentialChange), id, c.Status); return Result<bool>.Success(true);
    }, ct);
    public async Task<Result<TrainerFileResponse>> Document(int id, string? actor, CancellationToken ct)
    {
        var meta = await store.First(store.Query<TrainerDocument>().Where(x => x.Id == id).Select(x => new { x.TrainerApplicationId, x.TrainerProfileId, x.IsPublic }), ct);
        if (meta == null) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var allowed = meta.IsPublic && meta.TrainerProfileId != null && await store.Count(PublicProfiles().Where(x => x.Id == meta.TrainerProfileId), ct) > 0;
        if (!allowed && actor != null) allowed = await Role(actor, "Manager", ct) || (await Role(actor, "PT", ct) && await store.Count(store.Query<TrainerProfile>().Where(x => x.Id == meta.TrainerProfileId && x.PTUserId == actor), ct) > 0);
        if (!allowed) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var d = await store.First(store.Query<TrainerDocument>().Where(x => x.Id == id).Select(x => new TrainerFileResponse(x.FileName, x.ContentType, x.Content)), ct);
        return Result<TrainerFileResponse>.Success(d!);
    }
}
