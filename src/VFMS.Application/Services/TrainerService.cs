using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class TrainerService(ITrainerStore store, IPasswordHasher passwords, IMapper mapper)
{
    private static readonly TimeSpan SubmissionCooldown = TimeSpan.FromMinutes(10);

    private IQueryable<YogaClass> ActiveYogaAssignments => store.Query<YogaClass>().Where(c =>
        c.ClassStatus == YogaClassStatus.Draft || c.ClassStatus == YogaClassStatus.Published);

    private static Result<T> Error<T>(string code, string message, int status = 409) =>
        Result<T>.Failure(message, code, status);

    private async Task<bool> HasRole(string? actor, string role, CancellationToken ct) => actor != null &&
        await store.Count(from u in store.Query<ApplicationUser>()
                          join ur in store.Query<ApplicationUserRole>() on u.UserId equals ur.UserId
                          join r in store.Query<ApplicationRole>() on ur.RoleId equals r.Id
                          where u.UserId == actor && u.AccountStatus == AccountStatus.Active && !u.IsBanned && r.RoleName == role
                          select u, ct) > 0;

    private async Task<List<string>> BannedWords(string text, CancellationToken ct) =>
        (await store.List(store.Query<BannedWordList>().Where(x => x.IsActive).Select(x => x.Word), ct))
        .Where(w => !string.IsNullOrWhiteSpace(w) && BannedWordService.ContainsWholeWord(text, w))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Result<T> ContentRejected<T>(List<string> words) =>
        Error<T>(TrainerContentRules.RejectedCode, TrainerContentRules.Message(words), 400);

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

    private static List<T> Records<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];

    private void Mail(TrainerApplication application, string email, string subject, string body)
    {
        var queue = Records<TrainerQueuedEmail>(application.EmailQueueJson);
        queue.Add(store.Email(email, subject, body));
        application.EmailQueueJson = JsonSerializer.Serialize(queue);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private const string GmailSuffix = "@gmail.com";

    private async Task<string> SaveCertificates(IEnumerable<TrainerFileInput> files,
        IEnumerable<TrainerCertificateLinkInput> links, CancellationToken ct)
    {
        var saved = new List<TrainerStoredFile>();
        foreach (var file in files)
        {
            var stored = await store.SaveFile(file, ct);
            stored.ReplacesCertificateId = file.ReplacesCertificateId;
            stored.IsPublic = false;
            stored.IsAvatar = false;
            saved.Add(stored);
        }
        saved.AddRange(links.Select(link => new TrainerStoredFile
        {
            FileName = new Uri(link.Url).Host,
            ContentType = "text/uri-list",
            ExternalUrl = link.Url,
            ReplacesCertificateId = link.ReplacesCertificateId
        }));
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

    internal static IReadOnlyList<TrainerDocumentResponse> Docs(string json, string path, bool publicOnly = false) =>
        Records<TrainerStoredFile>(json).Where(d => !d.IsDeleted && !d.IsAvatar && (!publicOnly || d.IsPublic))
            .Select(d => new TrainerDocumentResponse(d.Id, d.FileName, d.ContentType,
                d.ExternalUrl ?? path + "/documents/" + Uri.EscapeDataString(d.Id), d.ExternalUrl is not null)).ToList();

    public async Task<Result<TrainerRecruitmentStatus>> RecruitmentStatus(CancellationToken ct)
    {
        var gym = await store.First(store.Query<Gym>().OrderBy(x => x.Id), ct);
        return gym == null
            ? Error<TrainerRecruitmentStatus>("TRAINER_CONFIGURATION", "Gym is not configured.", 503)
            : Result<TrainerRecruitmentStatus>.Success(new(gym.IsAcceptingTrainerApplications));
    }

    public Task<Result<TrainerRecruitmentStatus>> SetRecruitmentStatus(bool accepting, string actor, CancellationToken ct) =>
        store.Write(async () =>
        {
            if (!await HasRole(actor, "Manager", ct))
                return Error<TrainerRecruitmentStatus>("TRAINER_FORBIDDEN", "Manager role required.", 403);
            var gym = await store.First(store.Query<Gym>().OrderBy(x => x.Id), ct);
            if (gym == null)
                return Error<TrainerRecruitmentStatus>("TRAINER_CONFIGURATION", "Gym is not configured.", 503);
            if (gym.IsAcceptingTrainerApplications != accepting)
            {
                var before = gym.IsAcceptingTrainerApplications;
                gym.IsAcceptingTrainerApplications = accepting;
                Audit(actor, nameof(Gym), gym.Id, "TrainerRecruitmentStatus", before.ToString(), accepting.ToString());
            }
            return Result<TrainerRecruitmentStatus>.Success(new(accepting));
        }, ct);

    public Task<Result<TrainerApplicationReceipt>> Submit(SubmitTrainerApplicationRequest r, string? actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!(await new SubmitTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid)
            return Error<TrainerApplicationReceipt>("TRAINER_VALIDATION", "Invalid application fields or certificates.", 400);
        var update = r.ApplicationType == "ProfileUpdate";
        if (!update && await store.Count(store.Query<Gym>().Where(gym => gym.IsAcceptingTrainerApplications), ct) == 0)
            return Error<TrainerApplicationReceipt>("TRAINER_RECRUITMENT_CLOSED", "Trainer recruitment is closed.", 409);
        var pt = await HasRole(actor, "PT", ct);
        if (update && !pt)
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "PT role required.", 403);
        // UC 3.1: a signed-in Customer or Member may apply; members who only hold the Member role are included.
        if (!update && actor != null && !pt && !await HasRole(actor, "Customer", ct) && !await HasRole(actor, "Member", ct))
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Customer or Member role required.", 403);

        var email = r.Email.Trim().ToLowerInvariant();
        var citizen = r.CitizenId.Trim();
        var now = DateTime.UtcNow;
        if (!update && (pt || await IsEmployee(email, citizen, ct)))
            return Error<TrainerApplicationReceipt>("TRAINER_NOT_ELIGIBLE", "Staff, Manager, Admin and active Trainer accounts cannot apply.");
        if (update)
        {
            var owner = await store.First(store.Query<ApplicationUser>().Where(x => x.UserId == actor), ct);
            var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct);
            if (profile?.CitizenId == null)
                return Error<TrainerApplicationReceipt>("TRAINER_IDENTITY_MISSING", "The trainer profile has no verified Citizen ID. Contact the Manager.");
            if (owner == null || !string.Equals(owner.Email.Trim(), email, StringComparison.OrdinalIgnoreCase) || profile.CitizenId != citizen)
                return Error<TrainerApplicationReceipt>("TRAINER_IDENTITY_CONFLICT", "Email must match the current account and Citizen ID must match the trainer profile.");
            var replacements = r.Certificates.Select(file => file.ReplacesCertificateId)
                .Concat((r.CertificateLinks ?? []).Select(link => link.ReplacesCertificateId)).OfType<string>().ToArray();
            var published = Records<TrainerStoredFile>(profile.CertificatesJson);
            if (replacements.Distinct().Count() != replacements.Length ||
                replacements.Any(id => !published.Any(file => file.Id == id && !file.IsDeleted && !file.IsAvatar && file.IsPublic)))
                return Error<TrainerApplicationReceipt>("TRAINER_CERTIFICATE_TARGET_INVALID", "Select distinct published certificates to replace.", 400);
        }
        else if (r.Certificates.Any(file => file.ReplacesCertificateId != null) ||
                 (r.CertificateLinks ?? []).Any(link => link.ReplacesCertificateId != null))
            return Error<TrainerApplicationReceipt>("TRAINER_CERTIFICATE_TARGET_INVALID", "Recruitment cannot replace published certificates.", 400);
        var applications = store.Query<TrainerApplication>().Where(x => x.ApplicationType == r.ApplicationType);
        var history = update
            ? applications.Where(x => x.ApplicantUserId == actor || x.HiredUserId == actor)
            : RecruitmentHistory(applications, email, citizen);
        if (update && await store.Count(history.Where(x =>
                x.ApplicationStatus == TrainerApplicationStatus.Pending ||
                x.ApplicationStatus == TrainerApplicationStatus.UnderReview ||
                x.ApplicationStatus == TrainerApplicationStatus.Interviewing), ct) > 0)
            return Error<TrainerApplicationReceipt>("TRAINER_CHANGE_PENDING", "A profile update is already pending.");
        var last = await store.First(history.OrderByDescending(x => x.SubmittedAt), ct);
        if (last != null && last.SubmittedAt > now - SubmissionCooldown)
        {
            var errorCode = update ? "TRAINER_PROFILE_UPDATE_COOLDOWN" : "RECRUITMENT_COOLDOWN";
            var nextSubmissionAt = last.SubmittedAt + SubmissionCooldown;
            return Error<TrainerApplicationReceipt>(errorCode, $"You can submit again after {nextSubmissionAt:O}.");
        }

        var a = new TrainerApplication
        {
            ApplicantUserId = actor,
            ApplicationType = r.ApplicationType,
            CertificatesJson = await SaveCertificates(r.Certificates,
                (r.CertificateUrls ?? []).Select(url => new TrainerCertificateLinkInput(url))
                    .Concat(r.CertificateLinks ?? []), ct),
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
        if (!await HasRole(actor, "Manager", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        return await ApplicationList(r, store.Query<TrainerApplication>(), ct);
    }
    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> MyApplications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "PT", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "PT role required.", 403);
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
        if (!string.IsNullOrEmpty(r.Specialty)) q = q.Where(x => x.Specialty == r.Specialty);
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

    private TrainerApplicationResponse ApplicationResponse(TrainerApplication a) => mapper.Map<TrainerApplicationResponse>(a);
    public async Task<Result<TrainerApplicationResponse>> Application(int id, string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
        var a = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        if (a == null)
            return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Application not found.", 404);

        var applications = store.Query<TrainerApplication>().Where(x => x.ApplicationType == a.ApplicationType);
        var email = a.Email.Trim().ToLowerInvariant();
        var related = a.ApplicationType == "Recruitment"
            ? RecruitmentHistory(applications, email, a.CitizenId, a.Id)
            : applications.Where(x => x.Id == a.Id || x.Email.ToLower() == email ||
                (a.CitizenId != "" && x.CitizenId == a.CitizenId));
        var history = await store.List(related
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
            .Select(x => new TrainerSubmissionAttempt(x.Id, x.SubmissionAttempt, x.SubmittedAt, x.ApplicationStatus.ToString())), ct);
        TrainerAccountMatch? accountMatch = null;
        if (a.ApplicationType == "Recruitment" &&
            a.ApplicationStatus is TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview or TrainerApplicationStatus.Interviewing)
        {
            var matches = await FindMatchingAccounts(a, ct);
            accountMatch = DescribeAccountMatch(matches, await IneligibleAccounts(matches, ct), a);
            if (accountMatch is { Status: "Matched", UserId: { } matchedId })
                accountMatch = accountMatch with
                {
                    HasUnfinishedPTContract = await HasUnfinishedPTContract(matchedId, ct),
                    MemberServicesUntil = await MemberServicesUntil(matchedId, ct)
                };
        }
        var response = ApplicationResponse(a);
        if (a.ApplicationType == "ProfileUpdate")
        {
            // UC 3.3: show the Manager which published certificate each new one replaces.
            var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == a.ApplicantUserId), ct);
            var published = Records<TrainerStoredFile>(profile?.CertificatesJson).ToDictionary(f => f.Id, f => f.ExternalUrl ?? f.FileName);
            var targets = Records<TrainerStoredFile>(a.CertificatesJson).ToDictionary(f => f.Id, f => f.ReplacesCertificateId);
            response = response with
            {
                Certificates = response.Certificates.Select(d => targets.GetValueOrDefault(d.Id) is { } target &&
                    published.TryGetValue(target, out var name) ? d with { Replaces = name } : d).ToList()
            };
        }
        return Result<TrainerApplicationResponse>.Success(response with
        {
            SubmissionHistory = history,
            InterviewNote = a.InterviewNote,
            AccountMatch = accountMatch
        });
    }

    private static IQueryable<TrainerApplication> RecruitmentHistory(IQueryable<TrainerApplication> applications,
        string email, string citizenId, int? applicationId = null)
    {
        var isGmail = email.EndsWith(GmailSuffix, StringComparison.Ordinal);
        var identity = email.Replace(".", "");
        return applications.Where(x => x.Id == applicationId || (citizenId != "" && x.CitizenId == citizenId) ||
            x.Email.ToLower() == email || (isGmail && x.Email.ToLower().EndsWith(GmailSuffix) &&
                x.Email.ToLower().Replace(".", "") == identity));
    }

    private static readonly string[] StaffRoles = ["Staff", "Manager", "Admin"];

    // Email match that treats dotted and undotted @gmail.com variants as one address (GBR-03).
    private IQueryable<ApplicationUser> AccountsWithEmail(string email)
    {
        email = email.Trim().ToLowerInvariant();
        var isGmail = email.EndsWith(GmailSuffix, StringComparison.Ordinal);
        var identity = email.Replace(".", "");
        return store.Query<ApplicationUser>().Where(user => user.Email.ToLower() == email ||
            (isGmail && user.Email.ToLower().EndsWith(GmailSuffix) && user.Email.ToLower().Replace(".", "") == identity));
    }

    // GBR-101: Staff, Manager, Admin and active Trainer accounts cannot apply.
    private async Task<bool> IsEmployee(string email, string citizenId, CancellationToken ct) =>
        await store.Count(from u in AccountsWithEmail(email)
                          join ur in store.Query<ApplicationUserRole>() on u.UserId equals ur.UserId
                          join r in store.Query<ApplicationRole>() on ur.RoleId equals r.Id
                          where StaffRoles.Contains(r.RoleName) || (r.RoleName == "PT" && u.AccountStatus == AccountStatus.Active)
                          select u.Id, ct) > 0 ||
        await store.Count(from p in store.Query<TrainerProfile>()
                          join u in store.Query<ApplicationUser>() on p.PTUserId equals u.UserId
                          where p.CitizenId == citizenId && p.IsActive && u.AccountStatus == AccountStatus.Active
                          select p.Id, ct) > 0;

    private Task<List<ApplicationUser>> FindMatchingAccounts(TrainerApplication application, CancellationToken ct)
    {
        var email = application.Email.Trim().ToLowerInvariant();
        var isGmail = email.EndsWith(GmailSuffix, StringComparison.Ordinal);
        var identity = email.Replace(".", "");
        return store.List(store.Query<ApplicationUser>()
            .Where(user => user.Email.ToLower() == email || user.PhoneNumber == application.PhoneNumber ||
                (isGmail && user.Email.ToLower().EndsWith(GmailSuffix) && user.Email.ToLower().Replace(".", "") == identity)), ct);
    }

    // Only an active Customer/Member account can be linked on hire (UC-3.2 step 7, GBR-101).
    private async Task<HashSet<string>> IneligibleAccounts(IReadOnlyList<ApplicationUser> users, CancellationToken ct)
    {
        var ids = users.Select(u => u.UserId).ToList();
        var employees = await store.List((from ur in store.Query<ApplicationUserRole>()
                                          join r in store.Query<ApplicationRole>() on ur.RoleId equals r.Id
                                          where ids.Contains(ur.UserId) && (StaffRoles.Contains(r.RoleName) || r.RoleName == "PT")
                                          select ur.UserId).Distinct(), ct);
        return users.Where(u => u.AccountStatus != AccountStatus.Active || u.IsBanned).Select(u => u.UserId)
            .Concat(employees).ToHashSet();
    }

    private static readonly string[] MemberRoles = ["Customer", "Member"];

    // UC 3.2 step 7: a member who still trains under a PT contract cannot become a PT until that contract ends.
    // shortcut: the 14-day make-up period counts even without make-up units left; check MakeUpUnits if that blocks too often.
    private async Task<bool> HasUnfinishedPTContract(string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var makeUpFrom = DateOnly.FromDateTime(now.AddHours(7)).AddDays(-PersonalTrainingRules.MakeUpGraceDays);
        return await store.Count(store.Query<PTPurchase>().Where(p => p.MemberUserId == userId && !p.IsCompleted &&
            (p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt > now ||
             (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen) && p.EndDate >= makeUpFrom)), ct) > 0;
    }

    // UC 3.2 step 7: the latest date until which the account still has a Gym Membership or locker rental.
    private async Task<DateOnly?> MemberServicesUntil(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var gym = await store.First(store.Query<MemberMembership>().Where(m => m.MemberUserId == userId &&
                m.MembershipStatus == MembershipStatus.Active && m.EndDate >= today &&
                m.PackageComponent.ComponentType == ComponentType.GYM)
            .OrderByDescending(m => m.EndDate).Select(m => (DateOnly?)m.EndDate), ct);
        var locker = await store.First(store.Query<LockerRentalContract>().Where(c => c.CustomerUserId == userId &&
                (c.ContractStatus == LockerContractStatus.Active || c.ContractStatus == LockerContractStatus.Overdue ||
                 c.ContractStatus == LockerContractStatus.PendingHandover))
            .OrderByDescending(c => c.EndDate).Select(c => (DateOnly?)c.EndDate), ct);
        return gym > locker || locker is null ? gym : locker;
    }

    private static TrainerAccountMatch DescribeAccountMatch(IReadOnlyList<ApplicationUser> matches,
        IReadOnlySet<string> ineligible, TrainerApplication application) => matches.Count switch
    {
        0 => new("None"),
        1 when !ineligible.Contains(matches[0].UserId) => new("Matched", matches[0].UserId, matches[0].FullName),
        1 => new("Ineligible"),
        _ => new("Ambiguous", Matches: matches.OrderBy(user => user.UserId).Select(user =>
            new TrainerAccountCandidate(user.UserId, user.FullName, user.Email, user.PhoneNumber,
                string.Equals(user.Email, application.Email, StringComparison.OrdinalIgnoreCase) ||
                application.Email.EndsWith(GmailSuffix, StringComparison.OrdinalIgnoreCase) &&
                user.Email.EndsWith(GmailSuffix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(user.Email.Replace(".", ""), application.Email.Replace(".", ""), StringComparison.OrdinalIgnoreCase),
                user.PhoneNumber == application.PhoneNumber, !ineligible.Contains(user.UserId))).ToArray())
    };

    public Task<Result<TrainerApplicationResponse>> Review(int id, ReviewTrainerApplicationRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Manager role required.", 403);
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
                var published = Records<TrainerStoredFile>(profile.CertificatesJson);
                foreach (var file in Records<TrainerStoredFile>(a.CertificatesJson))
                {
                    if (file.ReplacesCertificateId is { } target)
                    {
                        var index = published.FindIndex(item => item.Id == target && item.IsPublic && !item.IsDeleted && !item.IsAvatar);
                        if (index < 0)
                            return Error<TrainerApplicationResponse>("TRAINER_CERTIFICATE_TARGET_INVALID", "A certificate to replace is no longer published.");
                        published.RemoveAt(index);
                    }
                    file.ReplacesCertificateId = null;
                    file.IsPublic = true;
                    file.IsAvatar = false;
                    published.Add(file);
                }
                profile.Specialty = a.Specialty;
                profile.CertificatesJson = JsonSerializer.Serialize(published);
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
            var matches = await FindMatchingAccounts(a, ct);
            var ineligible = await IneligibleAccounts(matches, ct);
            var accountMatch = DescribeAccountMatch(matches, ineligible, a);
            if (accountMatch.Status == "Ineligible")
                return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "The matching account is not an active Customer/Member account.");
            ApplicationUser? user;
            if (accountMatch.Status == "Ambiguous")
            {
                // UC-3.2 step 7: the Manager verifies identity manually and picks the applicant's account.
                user = matches.FirstOrDefault(m => m.UserId == r.ExistingUserId && !ineligible.Contains(m.UserId));
                if (user == null)
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Select one eligible matching account after verifying the applicant's identity.");
            }
            else
            {
                if (r.ExistingUserId != accountMatch.UserId)
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "The account match has changed. Reload the application and verify it again.");
                user = matches.SingleOrDefault();
            }
            var userId = user?.UserId;
            var previous = await store.First(store.Query<TrainerProfile>().Where(x => x.CitizenId == a.CitizenId ||
                (userId != null && x.PTUserId == userId)), ct);
            // A trainer removed under GBR-98 keeps an inactive profile; hiring the same person again reactivates it.
            var rehire = previous is { IsActive: false } && previous.PTUserId == userId && previous.CitizenId == a.CitizenId;
            if (previous != null && !rehire)
                return previous.CitizenId == a.CitizenId
                    ? Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Another trainer profile already uses this Citizen ID.")
                    : Error<TrainerApplicationResponse>("TRAINER_PROFILE_EXISTS", "Account already has a trainer profile.");
            if (user != null && await store.Count(ActiveYogaAssignments.Where(c => c.InstructorUserId == user.UserId), ct) > 0)
                return Error<TrainerApplicationResponse>("TRAINER_TEACHING_ROLE_CONFLICT",
                    "A Yoga instructor cannot be hired as a PT while assigned to Yoga classes.");
            if (user != null && await HasUnfinishedPTContract(user.UserId, ct))
                return Error<TrainerApplicationResponse>("TRAINER_MEMBER_PT_CONTRACT",
                    "The applicant still has a PT contract as a member. Hire them after that contract ends.");
            if (await BannedWords(a.Bio, ct) is { Count: > 0 } bannedBio) return ContentRejected<TrainerApplicationResponse>(bannedBio);
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
            // UC 3.2 step 7: a hired account holds only the PT role; its member history stays.
            foreach (var memberRole in await store.List(store.Query<ApplicationUserRole>().Where(x => x.UserId == user!.UserId &&
                         MemberRoles.Contains(x.Role.RoleName)), ct))
                store.Remove(memberRole);
            var profile = previous ?? new TrainerProfile { PTUserId = user!.UserId, CitizenId = a.CitizenId };
            profile.Bio = a.Bio;
            profile.Specialty = a.Specialty;
            profile.CertificatesJson = PublishCertificates(a.CertificatesJson);
            profile.Gender = a.Gender;
            profile.ExperienceYears = a.ExperienceYears;
            profile.Level = Enum.Parse<TrainerLevel>(r.Level!);
            profile.IsActive = true;
            // New and rehired trainers start 'Accepting New Members', so the GBR-98 60-day count does not run.
            profile.AcceptingNewMembers = true;
            profile.NotAcceptingSince = null;
            if (previous == null)
                store.Add(profile);
            await store.Save(ct);
            a.HiredUserId = user!.UserId;
            a.ApplicationStatus = TrainerApplicationStatus.Hired;
            // GBR-101: hiring closes the person's other open recruitment applications.
            var others = await store.List(RecruitmentHistory(store.Query<TrainerApplication>().Where(x => x.ApplicationType == "Recruitment"),
                    a.Email.Trim().ToLowerInvariant(), a.CitizenId)
                .Where(x => x.Id != a.Id && (x.ApplicationStatus == TrainerApplicationStatus.Pending ||
                    x.ApplicationStatus == TrainerApplicationStatus.UnderReview || x.ApplicationStatus == TrainerApplicationStatus.Interviewing)), ct);
            foreach (var other in others)
            {
                var otherBefore = other.ApplicationStatus.ToString();
                other.ApplicationStatus = TrainerApplicationStatus.Rejected;
                other.ReviewNote = $"Closed automatically: application {a.TrackingCode} was hired.";
                other.ReviewedByUserId = actor;
                other.ReviewedAt = DateTime.UtcNow;
                Audit(actor, nameof(TrainerApplication), other.Id, "AutoClose", otherBefore, other.ApplicationStatus.ToString());
            }
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
        if (r.Action == "Interview")
            a.InterviewNote = r.Note.Trim();
        else
            a.ReviewNote = r.Note.Trim();
        a.ReviewedByUserId = actor;
        a.ReviewedAt = DateTime.UtcNow;
        Audit(actor, nameof(TrainerApplication), a.Id, r.Action, before, a.ApplicationStatus.ToString());
        return Result<TrainerApplicationResponse>.Success(ApplicationResponse(a) with { InterviewNote = a.InterviewNote });
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
        if (!await HasRole(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Manager role required.", 403);
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
    private IQueryable<TrainerProfileResponse> PublicProfiles()
    {
        var yogaAssignments = ActiveYogaAssignments;
        return from profile in store.Query<TrainerProfile>()
               join user in store.Query<ApplicationUser>() on profile.PTUserId equals user.UserId
               where profile.IsActive && user.AccountStatus == AccountStatus.Active && !user.IsBanned &&
                   !yogaAssignments.Any(c => c.InstructorUserId == profile.PTUserId)
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
                   AverageRating = store.Query<TrainerReview>()
                       .Where(r => r.TrainerUserId == profile.PTUserId && r.IsVisible &&
                           r.ReviewStatus == TrainerReviewStatus.Approved)
                       .Select(r => (decimal?)r.Rating)
                       .Average() ?? 0,
                   IsAcceptingNewMembers = profile.AcceptingNewMembers,
                   NotAcceptingSince = profile.NotAcceptingSince
               };
    }

    /// <summary>UC 4.12 / GBR-98: the change applies immediately; members of untouched contracts get one week to change trainer.</summary>
    public Task<Result<TrainerReadinessStatus>> SetReadiness(bool accepting, string actor, CancellationToken ct) =>
        store.Write(async () =>
        {
            if (!await HasRole(actor, "PT", ct))
                return Error<TrainerReadinessStatus>("TRAINER_FORBIDDEN", "PT role required.", 403);
            var profile = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct);
            if (profile == null)
                return Error<TrainerReadinessStatus>("TRAINER_NOT_FOUND", "Active trainer profile not found.", 404);
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now.AddHours(7));
            var notified = 0;
            if (profile.AcceptingNewMembers != accepting)
            {
                var before = profile.AcceptingNewMembers.ToString();
                profile.AcceptingNewMembers = accepting;
                profile.NotAcceptingSince = accepting ? null : today;
                if (!accepting)
                    notified = await OfferTrainerChange(actor, now, today, ct);
                Audit(actor, nameof(TrainerProfile), profile.Id, "SetReadiness", before, accepting.ToString());
            }
            return Result<TrainerReadinessStatus>.Success(new(profile.AcceptingNewMembers, notified));
        }, ct);

    private async Task<int> OfferTrainerChange(string trainerId, DateTime now, DateOnly today, CancellationToken ct)
    {
        var untouched = await store.List(store.Query<PTPurchase>().Where(p => p.PTUserId == trainerId &&
            p.Status == PTPurchaseStatus.Active && !p.IsCompleted && p.EndDate >= today &&
            !p.Sessions.Any(s => s.SessionStatus == PTSessionStatus.Completed || s.SessionStatus == PTSessionStatus.AutoCompleted ||
                s.SessionStatus == PTSessionStatus.NoShow || s.SessionStatus == PTSessionStatus.Disputed)), ct);
        foreach (var contract in untouched)
        {
            contract.TrainerChangeDeadline = now.AddDays(7);
            store.Add(new Notification
            {
                RecipientUserId = contract.MemberUserId, SenderUserId = trainerId, NotificationType = "PersonalTraining",
                Title = "Bạn có thể đổi huấn luyện viên",
                Message = "Huấn luyện viên của bạn tạm ngừng nhận học viên mới. Bạn có 1 tuần để chọn huấn luyện viên khác cho các buổi còn lại.",
                EntityType = nameof(PTPurchase), EntityId = contract.Id, NotifiedAt = now
            });
        }
        return untouched.Count;
    }

    public async Task<Result<PagedResponse<TrainerProfileResponse>>> Search(TrainerSearch r, CancellationToken ct)
    {
        if (!(await new TrainerSearchValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerProfileResponse>>("TRAINER_VALIDATION", "Invalid filters.", 400);
        var q = PublicProfiles();
        if (r.Search != null)
            q = q.Where(x => x.FullName.Contains(r.Search));
        if (!string.IsNullOrEmpty(r.Specialty))
            q = q.Where(x => x.Specialty == r.Specialty);
        if (r.Gender != null)
            q = q.Where(x => x.Gender == r.Gender);
        if (r.MinimumRating != null)
            q = q.Where(x => x.AverageRating >= r.MinimumRating);
        return Result<PagedResponse<TrainerProfileResponse>>.Success(new(r.Page, r.PageSize, await store.Count(q, ct), await store.List(q.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize), ct)));
    }
    public async Task<Result<TrainerPublicDetail>> Public(int id, CancellationToken ct, string? actor = null, int reviewPage = 1)
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
        var hasActiveContract = await HasRole(actor, "Member", ct) &&
            await store.Count(store.Query<PTPurchase>().Where(x => x.MemberUserId == actor && x.PTUserId == p.UserId &&
                x.Status == PTPurchaseStatus.Active && !x.IsCompleted && x.EndDate >= today &&
                !x.PTFreezes.Any(f => (f.RequestStatus == PTFreezeRequestStatus.Approved ||
                    f.RequestStatus == PTFreezeRequestStatus.Active || f.RequestStatus == PTFreezeRequestStatus.Completed) &&
                    f.ActualStartDate <= today && f.ActualEndDate > today)), ct) > 0;
        const int pageSize = 20;
        var reviews = store.Query<TrainerReview>().Where(x => x.TrainerUserId == p.UserId &&
            x.IsVisible && x.ReviewStatus == TrainerReviewStatus.Approved);
        var reviewTotal = await store.Count(reviews, ct);
        reviewPage = Math.Clamp(reviewPage, 1, Math.Max(1, (int)Math.Ceiling((double)reviewTotal / pageSize)));
        var page = await store.List(reviews.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((reviewPage - 1) * pageSize).Take(pageSize)
            .Select(x => new TrainerReviewResponse(x.Rating, x.Comment, x.MemberUser.FullName)), ct);
        return Result<TrainerPublicDetail>.Success(new(p, certificates, availability, page,
            hasActiveContract, actor == p.UserId, reviewPage, reviewTotal));
    }
    public async Task<Result<TrainerPublicDetail>> Mine(string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "PT", ct)) return Error<TrainerPublicDetail>("TRAINER_FORBIDDEN", "PT role required.", 403);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor), ct);
        return p == null
            ? Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Profile not found.", 404)
            : await Public(p.Id, ct, actor);
    }
    public Task<Result<bool>> Update(UpdateTrainerProfileRequest r, string actor, CancellationToken ct) => store.Write(async () =>
    {
        if (!await HasRole(actor, "PT", ct)) return Error<bool>("TRAINER_FORBIDDEN", "PT role required.", 403);
        if (!(await new TrainerProfileValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Invalid bio or avatar.", 400);
        if (r.Bio != null && await BannedWords(r.Bio, ct) is { Count: > 0 } banned) return ContentRejected<bool>(banned);
        var p = await store.First(store.Query<TrainerProfile>().Where(x => x.PTUserId == actor && x.IsActive), ct);
        if (p == null)
            return Error<bool>("TRAINER_NOT_FOUND", "Profile not found.", 404);

        if (r.Bio != null)
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
    private const string SetupExpiredType = "TrainerSetupLinkExpired";

    /// <summary>GBR-04: tells Managers in-app, once per expired link, that a hired trainer's setup link expired unused.</summary>
    public Task<Result<int>> NotifyExpiredSetupLinks(CancellationToken ct) => store.Write(async () =>
    {
        var now = DateTime.UtcNow;
        var expired = await store.List(
            from a in store.Query<TrainerApplication>()
            join u in store.Query<ApplicationUser>() on a.HiredUserId equals u.UserId
            where a.ApplicationType == "Recruitment" && a.ApplicationStatus == TrainerApplicationStatus.Hired &&
                  u.AccountStatus == AccountStatus.PendingVerification && u.PasswordSetupExpiresAt <= now &&
                  !store.Query<Notification>().Any(n => n.NotificationType == SetupExpiredType &&
                      n.EntityType == nameof(TrainerApplication) && n.EntityId == a.Id && n.NotifiedAt >= u.PasswordSetupExpiresAt)
            select a, ct);
        if (expired.Count == 0)
            return Result<int>.Success(0);
        var managers = await store.List((from ur in store.Query<ApplicationUserRole>()
                                         join role in store.Query<ApplicationRole>() on ur.RoleId equals role.Id
                                         where role.RoleName == "Manager" select ur.UserId).Distinct(), ct);
        foreach (var application in expired)
            foreach (var manager in managers)
                Notify(manager, null, application, SetupExpiredType, "Trainer setup link expired",
                    $"{application.FullName} did not set a password within 48 hours. Resend the setup link from the application.");
        return Result<int>.Success(expired.Count);
    }, ct);

    private void Notify(string recipient, string? actor, TrainerApplication application, string type, string title, string message) =>
        store.Add(new Notification { RecipientUserId = recipient, SenderUserId = actor, NotificationType = type, Title = title, Message = message, EntityType = nameof(TrainerApplication), EntityId = application.Id, NotifiedAt = DateTime.UtcNow });

    private async Task<bool> OwnsApplication(TrainerApplication application, string? actor, CancellationToken ct) =>
        actor != null && (application.ApplicantUserId == actor || application.HiredUserId == actor) && await HasRole(actor, "PT", ct);

    public async Task<Result<TrainerFileResponse>> ApplicationDocument(int id, string fileId, string? actor, CancellationToken ct)
    {
        var application = await store.First(store.Query<TrainerApplication>().Where(x => x.Id == id), ct);
        if (application == null || (!await HasRole(actor, "Manager", ct) && !await OwnsApplication(application, actor, ct)))
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
        if (!allowed) allowed = await HasRole(actor, "Manager", ct) || (actor == profile.PTUserId && await HasRole(actor, "PT", ct));
        return allowed ? await ReadDocument(file, ct) : Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
    }
    private async Task<Result<TrainerFileResponse>> ReadDocument(TrainerStoredFile? file, CancellationToken ct)
    {
        if (file == null || file.IsDeleted || file.ExternalUrl is not null)
            return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404);
        var content = await store.ReadFile(file, ct);
        return content == null ? Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Document not found.", 404)
            : Result<TrainerFileResponse>.Success(new(file.FileName, file.ContentType, content));
    }
}
