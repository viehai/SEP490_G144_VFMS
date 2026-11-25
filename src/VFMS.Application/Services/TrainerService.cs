using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Settings;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class TrainerService(ITrainerRepository repo, IPasswordHasher passwords, IMapper mapper,
    TrainerSetupLinkSettings? setupLinks = null) : ITrainerService
{
    private static readonly TimeSpan SubmissionCooldown = TimeSpan.FromMinutes(10);
    // Stable queue discriminator for setup emails already persisted before localization.
    private const string SetupSubject = "Set up your trainer account";

    private static Result<T> Error<T>(string code, string message, int status = 409) =>
        Result<T>.Failure(message, code, status);

    private async Task<bool> HasRole(string? actor, string role, CancellationToken ct) =>
        actor != null && await repo.UserHasActiveRoleAsync(actor, role, ct);

    // A trainer signs in with the login role of their Trainer Role: PT or YogaInstructor.
    private const string YogaLoginRole = "YogaInstructor";
    private async Task<bool> IsTrainer(string? actor, CancellationToken ct) =>
        await HasRole(actor, "PT", ct) || await HasRole(actor, YogaLoginRole, ct);

    private async Task<List<string>> BannedWords(string text, CancellationToken ct) =>
        (await repo.GetActiveBannedWordsAsync(ct))
        .Where(w => !string.IsNullOrWhiteSpace(w) && BannedWordService.ContainsWholeWord(text, w))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // UC 3.2/3.3: a note is required for Interview and Reject only; Hire and Approve may leave it empty.
    private static string? Note(ReviewTrainerApplicationRequest r) => string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();

    private static Result<T> ContentRejected<T>(List<string> words) =>
        Error<T>(TrainerContentRules.RejectedCode, TrainerContentRules.Message(words), 400);

    private void Audit(string? actor, string entity, int id, string action, string? before = null, string? after = null) => repo.Add(new AuditLog
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

    private void Mail(TrainerApplication application, string email, string subject, string body) =>
        repo.QueueEmail(application, email, subject, body);

    // UC 3.1: the status page link is left out when no page URL is configured, so a submission never fails over it.
    private string TrackingLine(TrainerApplication application) =>
        setupLinks?.TryBuildTracking(application.TrackingCode) is { } link ? $" Xem trạng thái hồ sơ tại: {link}" : "";

    private async Task<TrainerApplicantPrefill?> ApplicantDetails(string actor, CancellationToken ct)
    {
        var user = await repo.GetUserAsync(actor, ct);
        if (user == null)
            return null;
        var physical = await repo.GetPhysicalProfileAsync(actor, ct);
        var gender = physical?.Gender is "Male" or "Female" or "Other" ? physical.Gender : null;
        return new(user.FullName, UserIdentityRules.NormalizeEmail(user.Email), user.PhoneNumber, physical?.DateOfBirth, gender);
    }

    public async Task<Result<TrainerApplicantPrefill>> ApplicantPrefill(string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "Customer", ct) && !await HasRole(actor, "Member", ct))
            return Error<TrainerApplicantPrefill>("TRAINER_FORBIDDEN", "Cần tài khoản khách hàng hoặc hội viên để thực hiện thao tác này.", 403);
        return await ApplicantDetails(actor, ct) is { } details
            ? Result<TrainerApplicantPrefill>.Success(details)
            : Error<TrainerApplicantPrefill>("TRAINER_NOT_FOUND", "Không tìm thấy tài khoản.", 404);
    }

    public async Task<Result<TrainerApplicationTracking>> Track(string trackingCode, CancellationToken ct)
    {
        var code = trackingCode?.Trim() ?? "";
        var a = code.Length is > 0 and <= 64 ? await repo.GetApplicationByTrackingCodeAsync(code, ct) : null;
        if (a == null)
            return Error<TrainerApplicationTracking>("TRAINER_APPLICATION_NOT_FOUND", "Không tìm thấy hồ sơ ứng với mã này.", 404);
        return Result<TrainerApplicationTracking>.Success(new(a.TrackingCode, a.ApplicationType, a.ApplicationStatus.ToString(),
            a.SubmittedAt, a.TrainerRole?.RoleName,
            a.ApplicationStatus == TrainerApplicationStatus.Rejected ? a.ReviewNote : null));
    }

    private static string HashSetupToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"VFMS:trainer-setup:v1:{token}")));

    private async Task<string> SaveCertificates(IEnumerable<TrainerFileInput> files,
        IEnumerable<TrainerCertificateLinkInput> links, CancellationToken ct)
    {
        var saved = new List<TrainerStoredFile>();
        foreach (var file in files)
        {
            var stored = await repo.SaveFileAsync(file, ct);
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
        var gym = await repo.GetDefaultGymAsync(ct);
        return gym == null
            ? Error<TrainerRecruitmentStatus>("TRAINER_CONFIGURATION", "Phòng tập chưa được cấu hình.", 503)
            : Result<TrainerRecruitmentStatus>.Success(new(gym.IsAcceptingTrainerApplications));
    }

    public Task<Result<TrainerRecruitmentStatus>> SetRecruitmentStatus(bool accepting, string actor, CancellationToken ct) =>
        repo.RunExclusiveAsync(async () =>
        {
            if (!await HasRole(actor, "Manager", ct))
                return Error<TrainerRecruitmentStatus>("TRAINER_FORBIDDEN", "Cần quyền quản lý để thực hiện thao tác này.", 403);
            var gym = await repo.GetDefaultGymAsync(ct);
            if (gym == null)
                return Error<TrainerRecruitmentStatus>("TRAINER_CONFIGURATION", "Phòng tập chưa được cấu hình.", 503);
            if (gym.IsAcceptingTrainerApplications != accepting)
            {
                var before = gym.IsAcceptingTrainerApplications;
                gym.IsAcceptingTrainerApplications = accepting;
                Audit(actor, nameof(Gym), gym.Id, "TrainerRecruitmentStatus", before.ToString(), accepting.ToString());
            }
            return Result<TrainerRecruitmentStatus>.Success(new(accepting));
        }, ct);

    public Task<Result<TrainerApplicationReceipt>> Submit(SubmitTrainerApplicationRequest r, string? actor, CancellationToken ct) => repo.RunExclusiveAsync(async () =>
    {
        if (!(await new SubmitTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid)
            return Error<TrainerApplicationReceipt>("TRAINER_VALIDATION", "Thông tin hồ sơ hoặc chứng chỉ không hợp lệ.", 400);
        var update = r.ApplicationType == "ProfileUpdate";
        // GBR-66: screen the public text at submission; a ProfileUpdate only publishes its specialization.
        if (await BannedWords(update ? r.Specialty ?? "" : r.Bio + " " + r.Specialty, ct) is { Count: > 0 } bannedText)
            return ContentRejected<TrainerApplicationReceipt>(bannedText);
        if (!update && !await repo.IsAnyGymRecruitingAsync(ct))
            return Error<TrainerApplicationReceipt>("TRAINER_RECRUITMENT_CLOSED", "Phòng tập đang tạm dừng nhận hồ sơ ứng tuyển.", 409);
        var trainer = await IsTrainer(actor, ct);
        if (update && !trainer)
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên để thực hiện thao tác này.", 403);
        // UC 3.1: a signed-in Customer or Member may apply; members who only hold the Member role are included.
        if (!update && actor != null && !trainer && !await HasRole(actor, "Customer", ct) && !await HasRole(actor, "Member", ct))
            return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Cần tài khoản khách hàng hoặc hội viên để thực hiện thao tác này.", 403);

        // UC 3.1: a signed-in Customer or Member applies with their account details; the form cannot change them.
        if (!update && actor != null && !trainer)
        {
            var locked = await ApplicantDetails(actor, ct);
            if (locked == null)
                return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Cần tài khoản khách hàng hoặc hội viên để thực hiện thao tác này.", 403);
            r = r with
            {
                FullName = locked.FullName,
                Email = locked.Email,
                PhoneNumber = locked.PhoneNumber,
                DateOfBirth = locked.DateOfBirth ?? r.DateOfBirth,
                Gender = locked.Gender ?? r.Gender
            };
            if (!(await new SubmitTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid)
                return Error<TrainerApplicationReceipt>("TRAINER_ACCOUNT_DETAILS_INVALID",
                    "Thông tin tài khoản (họ tên, email, số điện thoại hoặc ngày sinh) chưa hợp lệ để ứng tuyển. Vui lòng cập nhật hồ sơ cá nhân.", 400);
        }

        var email = UserIdentityRules.NormalizeEmail(r.Email);
        var phoneNumber = "";
        var now = DateTime.UtcNow;
        if (!update && !TrainerApplicantRules.TryNormalizePhone(r.PhoneNumber, out phoneNumber))
            return Error<TrainerApplicationReceipt>("TRAINER_VALIDATION", "Số điện thoại không hợp lệ.", 400);
        if (!update && (trainer ||
            actor != null && (await repo.GetUsersWithAnyRoleAsync([actor], StaffRoles, ct)).Count > 0 ||
            await IsEmployee(email, phoneNumber, ct)))
            return Error<TrainerApplicationReceipt>("TRAINER_NOT_ELIGIBLE", "Tài khoản nhân viên, quản lý, quản trị viên và huấn luyện viên đang hoạt động không được ứng tuyển.");
        int? trainerRoleId;
        if (update)
        {
            var owner = await repo.GetUserAsync(actor!, ct);
            var profile = await repo.GetProfileByUserAsync(actor!, activeOnly: true, ct);
            if (profile == null)
                return Error<TrainerApplicationReceipt>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ huấn luyện viên.", 404);
            if (owner == null)
                return Error<TrainerApplicationReceipt>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên để thực hiện thao tác này.", 403);
            // UC 3.3: identity and personal details come from the signed-in account and its TrainerProfile, not the form.
            email = UserIdentityRules.NormalizeEmail(owner.Email);
            phoneNumber = owner.PhoneNumber;
            r = r with
            {
                FullName = owner.FullName,
                Gender = profile.Gender,
                ExperienceYears = profile.ExperienceYears,
                Bio = profile.Bio ?? ""
            };
            var replacements = r.Certificates.Select(file => file.ReplacesCertificateId)
                .Concat((r.CertificateLinks ?? []).Select(link => link.ReplacesCertificateId)).OfType<string>().ToArray();
            var published = Records<TrainerStoredFile>(profile.CertificatesJson);
            if (replacements.Distinct().Count() != replacements.Length ||
                replacements.Any(id => !published.Any(file => file.Id == id && !file.IsDeleted && !file.IsAvatar && file.IsPublic)))
                return Error<TrainerApplicationReceipt>("TRAINER_CERTIFICATE_TARGET_INVALID", "Chọn các chứng chỉ đã công bố khác nhau để thay thế.", 400);
            // UC 3.3: a ProfileUpdate keeps the Trainer Role chosen at recruitment.
            trainerRoleId = profile.TrainerRoleId;
        }
        else
        {
            if (r.Certificates.Any(file => file.ReplacesCertificateId != null) ||
                (r.CertificateLinks ?? []).Any(link => link.ReplacesCertificateId != null))
                return Error<TrainerApplicationReceipt>("TRAINER_CERTIFICATE_TARGET_INVALID", "Hồ sơ ứng tuyển không được thay thế chứng chỉ đã công bố.", 400);
            trainerRoleId = await repo.GetActiveTrainerRoleIdAsync(r.TrainerRole ?? "", ct);
            if (trainerRoleId == null)
                return Error<TrainerApplicationReceipt>("TRAINER_CONFIGURATION", "Vai trò huấn luyện viên chưa được cấu hình.", 503);
        }
        var history = update
            ? await repo.GetApplicationsByUserAsync(r.ApplicationType, actor!, ct)
            : await repo.GetRecruitmentHistoryAsync(email, phoneNumber, null, actor, ct);
        if (update && history.Any(x => IsOpen(x.ApplicationStatus)))
            return Error<TrainerApplicationReceipt>("TRAINER_CHANGE_PENDING", "Bạn đang có yêu cầu cập nhật hồ sơ chờ duyệt.");
        var last = history.MaxBy(x => x.SubmittedAt);
        if (last != null && last.SubmittedAt > now - SubmissionCooldown)
        {
            var errorCode = update ? "TRAINER_PROFILE_UPDATE_COOLDOWN" : "RECRUITMENT_COOLDOWN";
            var nextSubmissionAt = last.SubmittedAt + SubmissionCooldown;
            return Error<TrainerApplicationReceipt>(errorCode, $"Bạn có thể nộp lại hồ sơ sau {nextSubmissionAt:O}.");
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
            PhoneNumber = phoneNumber,
            DateOfBirth = r.DateOfBirth,
            Gender = r.Gender,
            Specialty = (r.Specialty ?? "").Trim(),
            TrainerRoleId = trainerRoleId,
            ExperienceYears = r.ExperienceYears,
            Bio = r.Bio.Trim(),
            ConsentAt = now,
            SubmittedAt = now,
            TrackingCode = "TR-" + Guid.NewGuid().ToString("N"),
            SubmissionAttempt = history.Count + 1
        };
        repo.Add(a);
        await repo.SaveChangesAsync(ct);
        Mail(a, email, "Đã nhận hồ sơ ứng tuyển huấn luyện viên", $"Mã theo dõi hồ sơ của bạn là {a.TrackingCode}. Hồ sơ đang chờ xét duyệt.{TrackingLine(a)}");
        if (update)
        {
            var managers = await repo.GetActiveUserIdsInRoleAsync("Manager", ct);
            foreach (var manager in managers) Notify(manager, actor!, a, "TrainerCredentialReview", "Chứng chỉ huấn luyện viên đang chờ duyệt", "Huấn luyện viên đã gửi chứng chỉ để xét duyệt.");
        }
        Audit(actor, nameof(TrainerApplication), a.Id, "Submit", null, a.ApplicationStatus.ToString());
        return Result<TrainerApplicationReceipt>.Success(new(a.TrackingCode, "Pending", a.SubmissionAttempt), 201);
    }, ct);

    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> Applications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "Cần quyền quản lý để thực hiện thao tác này.", 403);
        return await ApplicationList(r, null, ct);
    }
    public async Task<Result<PagedResponse<TrainerApplicationResponse>>> MyApplications(TrainerApplicationFilter r, string actor, CancellationToken ct)
    {
        if (!await IsTrainer(actor, ct)) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên để thực hiện thao tác này.", 403);
        return await ApplicationList(r, actor, ct);
    }
    private async Task<Result<PagedResponse<TrainerApplicationResponse>>> ApplicationList(TrainerApplicationFilter r, string? owner, CancellationToken ct)
    {
        if (!(await new TrainerApplicationFilterValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerApplicationResponse>>("TRAINER_VALIDATION", "Bộ lọc tìm kiếm không hợp lệ.", 400);
        r = r with { PageSize = 10 };
        var (items, total) = await repo.SearchApplicationsAsync(r, owner, ct);
        var lastPage = Math.Max(1, (total + r.PageSize - 1) / r.PageSize);
        if (r.Page > lastPage)
        {
            r = r with { Page = lastPage };
            (items, total) = await repo.SearchApplicationsAsync(r, owner, ct);
        }
        var roles = await repo.GetTrainerRoleNamesAsync(ct);
        return Result<PagedResponse<TrainerApplicationResponse>>.Success(
            new(r.Page, r.PageSize, total, items.Select(a => ApplicationResponse(a, roles)).ToList()));
    }

    private TrainerApplicationResponse ApplicationResponse(TrainerApplication a, IReadOnlyDictionary<int, string> roles) =>
        mapper.Map<TrainerApplicationResponse>(a) with { TrainerRole = a.TrainerRoleId is { } id ? roles.GetValueOrDefault(id) : null };
    private async Task<TrainerApplicationResponse> ApplicationResponse(TrainerApplication a, CancellationToken ct) =>
        ApplicationResponse(a, await repo.GetTrainerRoleNamesAsync(ct));
    public async Task<Result<TrainerApplicationResponse>> Application(int id, string actor, CancellationToken ct)
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Cần quyền quản lý để thực hiện thao tác này.", 403);
        var a = await repo.GetApplicationAsync(id, ct);
        if (a == null)
            return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ ứng tuyển.", 404);

        var email = a.Email.Trim().ToLowerInvariant();
        IReadOnlyList<TrainerApplication> related = a.ApplicationType == "Recruitment"
            ? await repo.GetRecruitmentHistoryAsync(email, a.PhoneNumber, a.Id, a.ApplicantUserId ?? a.HiredUserId, ct)
            : a.ApplicantUserId is { } ownerId
                ? await repo.GetApplicationsByUserAsync("ProfileUpdate", ownerId, ct)
                : [a];
        var history = related
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
            .Select(x => new TrainerSubmissionAttempt(x.Id, x.SubmissionAttempt, x.SubmittedAt, x.ApplicationStatus.ToString())).ToList();
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
            else if (accountMatch is { Status: "Ambiguous", Matches: { } candidates })
            {
                // UC 3.2 step 7: the same PT contract and Gym/locker warnings apply to the account the Manager picks.
                var checkedCandidates = new List<TrainerAccountCandidate>();
                foreach (var candidate in candidates)
                    checkedCandidates.Add(!candidate.Eligible ? candidate : candidate with
                    {
                        HasUnfinishedPTContract = await HasUnfinishedPTContract(candidate.UserId, ct),
                        MemberServicesUntil = await MemberServicesUntil(candidate.UserId, ct)
                    });
                accountMatch = accountMatch with { Matches = checkedCandidates };
            }
        }
        var response = await ApplicationResponse(a, ct);
        if (a.ApplicationType == "ProfileUpdate")
        {
            // UC 3.3: show the Manager which published certificate each new one replaces.
            var profile = a.ApplicantUserId is { } applicant ? await repo.GetProfileByUserAsync(applicant, activeOnly: false, ct) : null;
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

    private static bool IsOpen(TrainerApplicationStatus status) => status is TrainerApplicationStatus.Pending or
        TrainerApplicationStatus.UnderReview or TrainerApplicationStatus.Interviewing;

    private static readonly string[] StaffRoles = ["Staff", "Manager", "Admin"];
    private static readonly string[] TrainerLoginRoles = [nameof(UserRole.PT), YogaLoginRole];
    private static readonly string[] EmployeeRoles = [.. StaffRoles, .. TrainerLoginRoles];

    // GBR-107: Staff, Manager, Admin and active Trainer (PT or Yoga) accounts cannot apply.
    private Task<bool> IsEmployee(string email, string phoneNumber, CancellationToken ct) =>
        repo.IsStaffOrActiveTrainerAsync(email, phoneNumber, ct);

    private async Task<IReadOnlyList<ApplicationUser>> FindMatchingAccounts(TrainerApplication application, CancellationToken ct)
    {
        // An authenticated applicant's UserId stays authoritative even if their contacts later change or are reused.
        if (application.ApplicantUserId is { } userId)
            return await repo.GetUserAsync(userId, ct) is { } applicant ? [applicant] : [];
        var email = UserIdentityRules.NormalizeEmail(application.Email);
        var hasPhone = UserIdentityRules.TryNormalizePhone(application.PhoneNumber, out var phoneNumber);
        return await repo.FindMatchingAccountsAsync(email, hasPhone ? phoneNumber : null, ct);
    }

    // Only an active Customer/Member account can be linked on hire (UC-3.2 step 7, GBR-107).
    private async Task<HashSet<string>> IneligibleAccounts(IReadOnlyList<ApplicationUser> users, CancellationToken ct)
    {
        var employees = await repo.GetUsersWithAnyRoleAsync(users.Select(u => u.UserId).ToList(), EmployeeRoles, ct);
        return users.Where(u => u.AccountStatus != AccountStatus.Active || u.IsBanned).Select(u => u.UserId)
            .Concat(employees).ToHashSet();
    }

    private static readonly string[] MemberRoles = ["Customer", "Member"];

    // UC 3.2 step 7: a member who still trains under a PT contract cannot become a PT until that contract ends.
    // shortcut: the 14-day make-up period counts even without make-up units left; check MakeUpUnits if that blocks too often.
    private Task<bool> HasUnfinishedPTContract(string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var makeUpFrom = DateOnly.FromDateTime(now.AddHours(7)).AddDays(-PersonalTrainingRules.MakeUpGraceDays);
        return repo.HasUnfinishedPTContractAsync(userId, now, makeUpFrom, ct);
    }

    // UC 3.2 step 7: the latest date until which the account still has a Gym Membership or locker rental.
    private async Task<DateOnly?> MemberServicesUntil(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var gym = await repo.GetLatestGymMembershipEndAsync(userId, today, ct);
        var locker = await repo.GetLatestLockerRentalEndAsync(userId, ct);
        return gym > locker || locker is null ? gym : locker;
    }

    private static bool EmailMatches(ApplicationUser user, TrainerApplication application) =>
        string.Equals(user.Email, application.Email, StringComparison.OrdinalIgnoreCase) ||
        application.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) &&
        user.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(user.Email.Replace(".", ""), application.Email.Replace(".", ""), StringComparison.OrdinalIgnoreCase);

    // Known accounts use UserId; a guest's phone-only match needs the Manager to verify and pick it.
    private static TrainerAccountMatch DescribeAccountMatch(IReadOnlyList<ApplicationUser> matches,
        IReadOnlySet<string> ineligible, TrainerApplication application) => matches.Count switch
        {
            0 when application.ApplicantUserId != null => new("Ineligible"),
            0 => new("None"),
            1 when ineligible.Contains(matches[0].UserId) => new("Ineligible"),
            1 when application.ApplicantUserId == matches[0].UserId || EmailMatches(matches[0], application) =>
                new("Matched", matches[0].UserId, matches[0].FullName),
            _ => new("Ambiguous", Matches: matches.OrderBy(user => user.UserId).Select(user =>
                new TrainerAccountCandidate(user.UserId, user.FullName, user.Email, user.PhoneNumber,
                    EmailMatches(user, application),
                    UserIdentityRules.TryNormalizePhone(application.PhoneNumber, out var phoneNumber) && user.PhoneNumber == phoneNumber,
                    !ineligible.Contains(user.UserId))).ToArray())
        };

    public Task<Result<TrainerApplicationResponse>> Review(int id, ReviewTrainerApplicationRequest r, string actor, CancellationToken ct) => repo.RunExclusiveAsync(async () =>
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<TrainerApplicationResponse>("TRAINER_FORBIDDEN", "Cần quyền quản lý để thực hiện thao tác này.", 403);
        if (!(await new ReviewTrainerApplicationValidator().ValidateAsync(r, ct)).IsValid) return Error<TrainerApplicationResponse>("TRAINER_VALIDATION", "Quyết định không hợp lệ. Vui lòng kiểm tra lại.", 400);
        var a = await repo.GetApplicationAsync(id, ct);
        if (a == null) return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ ứng tuyển.", 404);
        var before = a.ApplicationStatus.ToString();
        if (a.ApplicationType == "ProfileUpdate")
        {
            if (r.Action is not ("Approve" or "Reject") || a.ApplicationStatus is not (TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview))
                return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Yêu cầu cập nhật hồ sơ đang chờ chỉ có thể được duyệt hoặc từ chối.");
            var profile = a.ApplicantUserId is { } applicant ? await repo.GetProfileByUserAsync(applicant, activeOnly: true, ct) : null;
            if (profile == null) return Error<TrainerApplicationResponse>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ huấn luyện viên.", 404);
            if (r.Action == "Approve")
            {
                // GBR-66: the requested specialization is public text.
                if (await BannedWords(a.Specialty, ct) is { Count: > 0 } bannedSpecialty)
                    return ContentRejected<TrainerApplicationResponse>(bannedSpecialty);
                var published = Records<TrainerStoredFile>(profile.CertificatesJson);
                foreach (var file in Records<TrainerStoredFile>(a.CertificatesJson))
                {
                    if (file.ReplacesCertificateId is { } target)
                    {
                        var index = published.FindIndex(item => item.Id == target && item.IsPublic && !item.IsDeleted && !item.IsAvatar);
                        if (index < 0)
                            return Error<TrainerApplicationResponse>("TRAINER_CERTIFICATE_TARGET_INVALID", "Chứng chỉ cần thay thế không còn được công bố.");
                        published.RemoveAt(index);
                    }
                    file.ReplacesCertificateId = null;
                    file.IsPublic = true;
                    file.IsAvatar = false;
                    published.Add(file);
                }
                // An empty specialization in the request keeps the published one (UC 3.3).
                if (!string.IsNullOrWhiteSpace(a.Specialty))
                    profile.Specialty = a.Specialty;
                profile.CertificatesJson = JsonSerializer.Serialize(published);
            }
            a.ApplicationStatus = r.Action == "Approve" ? TrainerApplicationStatus.Approved : TrainerApplicationStatus.Rejected;
            a.ReviewNote = Note(r);
            a.ReviewedByUserId = actor;
            a.ReviewedAt = DateTime.UtcNow;
            Notify(profile.PTUserId, actor, a, "TrainerCredentialDecision", "Đã xét duyệt chứng chỉ huấn luyện viên", $"Yêu cầu cập nhật hồ sơ của bạn đã {(a.ApplicationStatus == TrainerApplicationStatus.Approved ? "được duyệt" : "bị từ chối")}.");
            Audit(actor, nameof(TrainerApplication), a.Id, r.Action, before, a.ApplicationStatus.ToString());
            return Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct));
        }
        if (r.Action == "Approve") return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Vui lòng chọn ghi nhận phỏng vấn, tuyển dụng hoặc từ chối cho hồ sơ ứng tuyển.");
        if (a.ApplicationStatus == TrainerApplicationStatus.Hired && r.Action == "Hire")
            return Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct));
        if (a.ApplicationStatus is not (TrainerApplicationStatus.Pending or TrainerApplicationStatus.UnderReview or TrainerApplicationStatus.Interviewing))
            return Error<TrainerApplicationResponse>("TRAINER_INVALID_STATE", "Chỉ có thể xét duyệt hồ sơ đang chờ hoặc đang phỏng vấn.");
        // UC 3.2 step 5 (changed, see NewScope.txt): the interview is recorded once; after that the Manager hires or rejects.
        if (r.Action == "Interview" && a.ApplicationStatus == TrainerApplicationStatus.Interviewing)
            return Error<TrainerApplicationResponse>("TRAINER_INTERVIEW_ALREADY_RECORDED",
                "Hồ sơ đã được ghi nhận phỏng vấn. Chỉ ghi nhận phỏng vấn một lần, hãy tuyển dụng hoặc từ chối.");
        if (r.Action == "Hire")
        {
            if (a.ApplicationStatus is not (TrainerApplicationStatus.Interviewing or TrainerApplicationStatus.UnderReview)) return Error<TrainerApplicationResponse>("TRAINER_INTERVIEW_REQUIRED", "Cần ghi nhận phỏng vấn trước khi tuyển dụng.");
            var matches = await FindMatchingAccounts(a, ct);
            var ineligible = await IneligibleAccounts(matches, ct);
            var accountMatch = DescribeAccountMatch(matches, ineligible, a);
            if (accountMatch.Status == "Ineligible")
                return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Tài khoản khớp không phải tài khoản khách hàng hoặc hội viên đang hoạt động.");
            ApplicationUser? user;
            if (accountMatch.Status == "Ambiguous")
            {
                // UC-3.2 step 7: the Manager verifies identity manually and picks the applicant's account.
                user = matches.FirstOrDefault(m => m.UserId == r.ExistingUserId && !ineligible.Contains(m.UserId));
                if (user == null)
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Sau khi xác minh danh tính ứng viên, hãy chọn một tài khoản khớp đủ điều kiện.");
            }
            else
            {
                if (r.ExistingUserId != accountMatch.UserId)
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Tài khoản khớp đã thay đổi. Vui lòng tải lại hồ sơ và xác minh lại.");
                user = matches.SingleOrDefault();
            }
            var userId = user?.UserId;
            var previous = userId == null ? null : await repo.FindProfileForHireAsync(userId, ct);
            // A trainer removed under GBR-104 keeps an inactive profile; hiring the same person again reactivates it.
            var rehire = previous is { IsActive: false } && previous.PTUserId == userId;
            if (previous != null && !rehire)
                return Error<TrainerApplicationResponse>("TRAINER_PROFILE_EXISTS", "Tài khoản đã có hồ sơ huấn luyện viên.");
            // UC 3.2 step 7: the login role comes from the applied Trainer Role (PT -> PT, Yoga -> YogaInstructor).
            var trainerRole = await repo.GetTrainerRoleAsync(a.TrainerRoleId, ct);
            var role = trainerRole?.Role;
            if (trainerRole?.RoleName == TrainerRoleNames.PT && user != null &&
                await repo.HasActiveYogaAssignmentsAsync(user.UserId, ct))
                return Error<TrainerApplicationResponse>("TRAINER_TEACHING_ROLE_CONFLICT",
                    "Huấn luyện viên Yoga đang được phân công lớp không thể được tuyển làm PT.");
            if (user != null && await HasUnfinishedPTContract(user.UserId, ct))
                return Error<TrainerApplicationResponse>("TRAINER_MEMBER_PT_CONTRACT",
                    "Ứng viên còn hợp đồng PT với tư cách hội viên. Chỉ có thể tuyển sau khi hợp đồng đó kết thúc.");
            if (await BannedWords(a.Bio + " " + a.Specialty, ct) is { Count: > 0 } bannedBio) return ContentRejected<TrainerApplicationResponse>(bannedBio);
            var gym = await repo.GetDefaultGymAsync(ct);
            if (role == null || gym == null) return Error<TrainerApplicationResponse>("TRAINER_CONFIGURATION", "Phòng tập hoặc vai trò huấn luyện viên chưa được cấu hình.", 503);
            var isNew = user == null;
            if (isNew)
            {
                var email = UserIdentityRules.NormalizeEmail(a.Email);
                if (!UserIdentityRules.IsValidEmail(email) || !UserIdentityRules.TryNormalizePhone(a.PhoneNumber, out var phoneNumber))
                    return Error<TrainerApplicationResponse>("TRAINER_IDENTITY_CONFLICT", "Email hoặc số điện thoại trong hồ sơ không hợp lệ.");

                user = new ApplicationUser
                {
                    UserId = Guid.NewGuid().ToString(),
                    GymId = gym.Id,
                    FullName = a.FullName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    AccountStatus = AccountStatus.PendingVerification,
                    PasswordHash = passwords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
                };
                repo.Add(user);
            }
            if (!await repo.UserHasRoleIdAsync(user!.UserId, role.Id, ct)) repo.Add(new ApplicationUserRole { UserId = user.UserId, RoleId = role.Id });
            // UC 3.2 step 7: a hired account holds only its trainer login role; its member history stays.
            foreach (var memberRole in await repo.GetRoleAssignmentsAsync(user.UserId, MemberRoles, ct))
                repo.Remove(memberRole);
            var profile = previous ?? new TrainerProfile { PTUserId = user!.UserId };
            profile.Bio = a.Bio;
            profile.Specialty = a.Specialty;
            profile.TrainerRoleId = a.TrainerRoleId;
            profile.CertificatesJson = PublishCertificates(a.CertificatesJson);
            profile.Gender = a.Gender;
            profile.ExperienceYears = a.ExperienceYears;
            profile.Level = Enum.Parse<TrainerLevel>(r.Level!);
            profile.IsActive = true;
            // New and rehired trainers start 'Accepting New Members', so the GBR-104 60-day count does not run.
            profile.AcceptingNewMembers = true;
            profile.NotAcceptingSince = null;
            profile.NotAcceptingCountFrom = null;
            profile.NotAcceptingAccumulatedDays = 0;
            profile.AcceptingSince = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            if (previous == null)
                repo.Add(profile);
            await repo.SaveChangesAsync(ct);
            a.HiredUserId = user!.UserId;
            a.ApplicationStatus = TrainerApplicationStatus.Hired;
            // GBR-107: hiring closes the person's other open recruitment applications.
            var others = (await repo.GetRecruitmentHistoryAsync(UserIdentityRules.NormalizeEmail(a.Email), a.PhoneNumber, null, user.UserId, ct))
                .Where(x => x.Id != a.Id && IsOpen(x.ApplicationStatus) &&
                    (x.ApplicantUserId == null || x.ApplicantUserId == user.UserId) &&
                    (x.HiredUserId == null || x.HiredUserId == user.UserId)).ToList();
            foreach (var other in others)
            {
                var otherBefore = other.ApplicationStatus.ToString();
                other.ApplicationStatus = TrainerApplicationStatus.Rejected;
                other.ReviewNote = $"Tự động đóng: ứng viên đã được tuyển theo hồ sơ {a.TrackingCode}.";
                other.ReviewedByUserId = actor;
                other.ReviewedAt = DateTime.UtcNow;
                Audit(actor, nameof(TrainerApplication), other.Id, "AutoClose", otherBefore, other.ApplicationStatus.ToString());
            }
            if (isNew)
                QueueSetup(user, a);
            else
                Mail(a, user.Email, "Đã kích hoạt quyền huấn luyện viên", $"Tài khoản {user.Email} của bạn đã có quyền huấn luyện viên {(trainerRole!.RoleName == TrainerRoleNames.Yoga ? "Yoga" : "cá nhân")}. Hãy đăng nhập bằng email {user.Email} và mật khẩu hiện tại; vui lòng đăng nhập lại để cập nhật quyền truy cập.");
        }
        else
        {
            a.ApplicationStatus = r.Action == "Interview" ? TrainerApplicationStatus.Interviewing : TrainerApplicationStatus.Rejected;
            if (r.Action == "Reject")
                Mail(a, a.Email, "Kết quả xét duyệt hồ sơ ứng tuyển huấn luyện viên", $"Hồ sơ {a.TrackingCode} đã bị từ chối. Lý do: {Note(r)}.{TrackingLine(a)}");
            else if (before != nameof(TrainerApplicationStatus.Interviewing))
                Mail(a, a.Email, "Hồ sơ ứng tuyển huấn luyện viên đang được phỏng vấn", $"Hồ sơ {a.TrackingCode} đã chuyển sang trạng thái đang phỏng vấn. Vui lòng chờ phòng tập liên hệ.{TrackingLine(a)}");
        }
        if (r.Action == "Interview")
        {
            a.InterviewNote = $"[{DateTime.UtcNow.AddHours(7):dd/MM/yyyy HH:mm}] {Note(r)}";
        }
        else
            a.ReviewNote = Note(r);
        a.ReviewedByUserId = actor;
        a.ReviewedAt = DateTime.UtcNow;
        Audit(actor, nameof(TrainerApplication), a.Id, r.Action, before, a.ApplicationStatus.ToString());
        return Result<TrainerApplicationResponse>.Success(await ApplicationResponse(a, ct) with { InterviewNote = a.InterviewNote });
    }, ct);
    private void QueueSetup(ApplicationUser user, TrainerApplication application)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordSetupTokenHash = HashSetupToken(token);
        // GBR-04: the 48 hours start when TrainerEmailWorker sends the link; until then the link cannot be used.
        user.PasswordSetupExpiresAt = null;
        Mail(application, user.Email, SetupSubject, "SETUP:" + token);
    }
    public Task<Result<bool>> ResendSetup(int applicationId, string actor, CancellationToken ct) => repo.RunExclusiveAsync(async () =>
    {
        if (!await HasRole(actor, "Manager", ct)) return Error<bool>("TRAINER_FORBIDDEN", "Cần quyền quản lý để thực hiện thao tác này.", 403);
        var a = await repo.GetHiredRecruitmentAsync(applicationId, ct);
        var loginRoleId = a?.TrainerRoleId is { } trainerRoleId
            ? await repo.GetActiveLoginRoleIdAsync(trainerRoleId, TrainerLoginRoles, ct)
            : null;
        var u = a?.HiredUserId is { } hiredId && loginRoleId is { } roleId
            ? await repo.GetPendingTrainerAccountAsync(hiredId, roleId, ct)
            : null;
        if (u == null) return Error<bool>("TRAINER_INVALID_STATE", "Chỉ tài khoản đã được tuyển và đang chờ kích hoạt mới được nhận liên kết thiết lập mật khẩu.");
        await ClearSetupEmails(u, ct);
        QueueSetup(u, a!);
        var status = a!.ApplicationStatus.ToString();
        Audit(actor, nameof(TrainerApplication), applicationId, "ResendSetup", status, status);
        return Result<bool>.Success(true);
    }, ct);
    public async Task<Result<bool>> Setup(TrainerSetupRequest r, CancellationToken ct)
    {
        if (!(await new TrainerSetupValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Mã thiết lập hoặc định dạng mật khẩu không hợp lệ.", 400);
        var tokenHash = HashSetupToken(r.Token);
        var user = await repo.FindEligiblePasswordSetupAsync(tokenHash, DateTime.UtcNow, ct);
        if (user is null)
            return InvalidSetupToken();

        var passwordHash = passwords.Hash(r.Password);
        return await repo.RunExclusiveAsync(async () =>
        {
            if (!await repo.TryCompletePasswordSetupAsync(user.UserId, tokenHash, passwordHash, DateTime.UtcNow, ct))
                return InvalidSetupToken();

            await ClearSetupEmails(user, ct);
            Audit(user.UserId, nameof(ApplicationUser), user.Id, "TrainerPasswordSetup",
                nameof(AccountStatus.PendingVerification), nameof(AccountStatus.Active));
            return Result<bool>.Success(true);
        }, ct);
    }

    private static Result<bool> InvalidSetupToken() => Error<bool>("TRAINER_SETUP_INVALID", "Liên kết thiết lập mật khẩu không hợp lệ hoặc đã hết hạn.", 400);

    /// <summary>UC 4.12 / GBR-104: the change applies immediately; members of untouched contracts get one week to change trainer.</summary>
    public Task<Result<TrainerReadinessStatus>> SetReadiness(bool accepting, string actor, CancellationToken ct) =>
        repo.RunExclusiveAsync(async () =>
        {
            if (!await HasRole(actor, "PT", ct))
                return Error<TrainerReadinessStatus>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên cá nhân để thực hiện thao tác này.", 403);
            var profile = await repo.GetProfileByUserAsync(actor, activeOnly: true, ct);
            if (profile == null)
                return Error<TrainerReadinessStatus>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ huấn luyện viên đang hoạt động.", 404);
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now.AddHours(7));
            var notified = 0;
            if (profile.AcceptingNewMembers != accepting)
            {
                var before = profile.AcceptingNewMembers.ToString();
                var running = accepting && await repo.HasRunningPTContractAsync(actor, today, ct);
                TrainerReadinessRules.SetAccepting(profile, accepting, today, running);
                if (!accepting)
                    notified = await OfferTrainerChange(actor, now, today, ct);
                else
                    // GBR-104: switching back to 'Accepting' closes the one-week window to change trainer.
                    foreach (var contract in await repo.GetContractsWithTrainerChangeDeadlineAsync(actor, ct))
                        contract.TrainerChangeDeadline = null;
                Audit(actor, nameof(TrainerProfile), profile.Id, "SetReadiness", before, accepting.ToString());
            }
            return Result<TrainerReadinessStatus>.Success(new(profile.AcceptingNewMembers, notified));
        }, ct);

    private async Task<int> OfferTrainerChange(string trainerId, DateTime now, DateOnly today, CancellationToken ct)
    {
        var untouched = await repo.GetUntouchedRunningContractsAsync(trainerId, today, ct);
        foreach (var contract in untouched)
        {
            contract.TrainerChangeDeadline = now.AddDays(7);
            repo.Add(new Notification
            {
                RecipientUserId = contract.MemberUserId,
                SenderUserId = trainerId,
                NotificationType = "PersonalTraining",
                Title = "Bạn có thể đổi huấn luyện viên",
                Message = "Huấn luyện viên của bạn tạm ngừng nhận học viên mới. Bạn có 1 tuần để chọn huấn luyện viên khác cho các buổi còn lại.",
                EntityType = nameof(PTPurchase),
                EntityId = contract.Id,
                NotifiedAt = now
            });
        }
        return untouched.Count;
    }

    public async Task<Result<PagedResponse<TrainerProfileResponse>>> Search(TrainerSearch r, CancellationToken ct)
    {
        if (!(await new TrainerSearchValidator().ValidateAsync(r, ct)).IsValid) return Error<PagedResponse<TrainerProfileResponse>>("TRAINER_VALIDATION", "Bộ lọc tìm kiếm không hợp lệ.", 400);
        r = r with { PageSize = 10 };
        var (items, total) = await repo.SearchPublicProfilesAsync(r, ct);
        var lastPage = Math.Max(1, (total + r.PageSize - 1) / r.PageSize);
        if (r.Page > lastPage)
        {
            r = r with { Page = lastPage };
            (items, total) = await repo.SearchPublicProfilesAsync(r, ct);
        }
        return Result<PagedResponse<TrainerProfileResponse>>.Success(new(r.Page, r.PageSize, total, items));
    }
    public async Task<Result<TrainerPublicDetail>> Public(int id, CancellationToken ct, string? actor = null, int reviewPage = 1)
    {
        var p = await repo.GetPublicProfileAsync(id, ct);
        if (p == null)
            return Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Không tìm thấy huấn luyện viên.", 404);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var stored = await repo.GetProfileAsync(id, ct);
        var certificates = Docs(stored!.CertificatesJson, $"/api/trainers/{id}", true);
        if (Records<TrainerStoredFile>(stored.CertificatesJson).Count == 0 && !string.IsNullOrWhiteSpace(stored.CertificateUrl))
            certificates = [new TrainerDocumentResponse("legacy", "Chứng chỉ", "application/octet-stream", stored.CertificateUrl)];
        // UC 3.4 step 7: a frozen contract is still the member's package, so its schedule stays reachable.
        var hasActiveContract = (await HasRole(actor, "Member", ct) || await HasRole(actor, "Customer", ct)) &&
            await repo.HasRunningContractWithTrainerAsync(actor!, p.UserId, today, ct);
        const int pageSize = 10;
        // GBR-108: only PT contract reviews (UC 4.10) belong to a trainer.
        var reviewTotal = await repo.CountPublishedReviewsAsync(p.UserId, ct);
        reviewPage = Math.Clamp(reviewPage, 1, Math.Max(1, (int)Math.Ceiling((double)reviewTotal / pageSize)));
        var page = await repo.GetPublishedReviewsAsync(p.UserId, (reviewPage - 1) * pageSize, pageSize, ct);
        return Result<TrainerPublicDetail>.Success(new(p, certificates, page,
            hasActiveContract, actor == p.UserId, reviewPage, reviewTotal));
    }
    public async Task<Result<TrainerPublicDetail>> Mine(string actor, CancellationToken ct)
    {
        if (!await IsTrainer(actor, ct)) return Error<TrainerPublicDetail>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên để thực hiện thao tác này.", 403);
        var p = await repo.GetProfileByUserAsync(actor, activeOnly: false, ct);
        return p == null
            ? Error<TrainerPublicDetail>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ huấn luyện viên.", 404)
            : await Public(p.Id, ct, actor);
    }
    public Task<Result<bool>> Update(UpdateTrainerProfileRequest r, string actor, CancellationToken ct) => repo.RunExclusiveAsync(async () =>
    {
        if (!await IsTrainer(actor, ct)) return Error<bool>("TRAINER_FORBIDDEN", "Cần quyền huấn luyện viên để thực hiện thao tác này.", 403);
        if (!(await new TrainerProfileValidator().ValidateAsync(r, ct)).IsValid) return Error<bool>("TRAINER_VALIDATION", "Giới thiệu hoặc ảnh đại diện không hợp lệ.", 400);
        if (r.Bio != null && await BannedWords(r.Bio, ct) is { Count: > 0 } banned) return ContentRejected<bool>(banned);
        var p = await repo.GetProfileByUserAsync(actor, activeOnly: true, ct);
        if (p == null)
            return Error<bool>("TRAINER_NOT_FOUND", "Không tìm thấy hồ sơ huấn luyện viên.", 404);

        if (r.Bio != null)
            p.Bio = r.Bio.Trim();
        if (r.Avatar != null)
        {
            var d = await repo.SaveFileAsync(r.Avatar, ct);
            d.IsAvatar = true;
            d.IsPublic = true;
            p.AvatarJson = JsonSerializer.Serialize(d);
        }
        Audit(actor, nameof(TrainerProfile), p.Id, "UpdatePublicProfile", "Active", "Active");
        return Result<bool>.Success(true);
    }, ct);
    private async Task ClearSetupEmails(ApplicationUser user, CancellationToken ct)
    {
        foreach (var application in await repo.GetApplicationsLinkedToUserAsync(user, ct))
            repo.RemoveUnsentEmails(application, SetupSubject);
    }
    private const string SetupExpiredType = "TrainerSetupLinkExpired";

    /// <summary>GBR-04: tells Managers in-app, once per expired link, that a hired trainer's setup link expired unused.</summary>
    public Task<Result<int>> NotifyExpiredSetupLinks(CancellationToken ct) => repo.RunExclusiveAsync(async () =>
    {
        var expired = await repo.GetExpiredSetupLinkApplicationsAsync(DateTime.UtcNow, SetupExpiredType, ct);
        if (expired.Count == 0)
            return Result<int>.Success(0);
        var managers = await repo.GetActiveUserIdsInRoleAsync("Manager", ct);
        foreach (var application in expired)
            foreach (var manager in managers)
                Notify(manager, null, application, SetupExpiredType, "Liên kết thiết lập mật khẩu huấn luyện viên đã hết hạn",
                    $"{application.FullName} chưa thiết lập mật khẩu trong vòng 48 giờ. Vui lòng gửi lại liên kết từ hồ sơ ứng tuyển.");
        return Result<int>.Success(expired.Count);
    }, ct);

    private void Notify(string recipient, string? actor, TrainerApplication application, string type, string title, string message) =>
        repo.Add(new Notification { RecipientUserId = recipient, SenderUserId = actor, NotificationType = type, Title = title, Message = message, EntityType = nameof(TrainerApplication), EntityId = application.Id, NotifiedAt = DateTime.UtcNow });

    private async Task<bool> OwnsApplication(TrainerApplication application, string? actor, CancellationToken ct) =>
        actor != null && (application.ApplicantUserId == actor || application.HiredUserId == actor) && await IsTrainer(actor, ct);

    public async Task<Result<TrainerFileResponse>> ApplicationDocument(int id, string fileId, string? actor, CancellationToken ct)
    {
        var application = await repo.GetApplicationAsync(id, ct);
        if (application == null || (!await HasRole(actor, "Manager", ct) && !await OwnsApplication(application, actor, ct)))
            return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
        return await ReadDocument(Records<TrainerStoredFile>(application.CertificatesJson).FirstOrDefault(x => x.Id == fileId && !x.IsAvatar), ct);
    }
    public async Task<Result<TrainerFileResponse>> Document(int id, string fileId, string? actor, CancellationToken ct) =>
        await ProfileFile(id, fileId, false, actor, ct);

    public async Task<Result<TrainerFileResponse>> Avatar(int id, string? actor, CancellationToken ct) =>
        await ProfileFile(id, null, true, actor, ct);

    private async Task<Result<TrainerFileResponse>> ProfileFile(int id, string? fileId, bool avatar, string? actor, CancellationToken ct)
    {
        var profile = await repo.GetProfileAsync(id, ct);
        if (profile == null) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
        var file = avatar
            ? (string.IsNullOrWhiteSpace(profile.AvatarJson) ? null : JsonSerializer.Deserialize<TrainerStoredFile>(profile.AvatarJson))
            : Records<TrainerStoredFile>(profile.CertificatesJson).FirstOrDefault(x => x.Id == fileId && !x.IsAvatar);
        if (file == null || file.IsDeleted) return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
        var allowed = file.IsPublic && await repo.GetPublicProfileAsync(id, ct) != null;
        if (!allowed) allowed = await HasRole(actor, "Manager", ct) || (actor == profile.PTUserId && await IsTrainer(actor, ct));
        return allowed ? await ReadDocument(file, ct) : Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
    }
    private async Task<Result<TrainerFileResponse>> ReadDocument(TrainerStoredFile? file, CancellationToken ct)
    {
        if (file == null || file.IsDeleted || file.ExternalUrl is not null)
            return Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
        var content = await repo.ReadFileAsync(file, ct);
        return content == null ? Error<TrainerFileResponse>("TRAINER_NOT_FOUND", "Không tìm thấy tài liệu.", 404)
            : Result<TrainerFileResponse>.Success(new(file.FileName, file.ContentType, content));
    }

    /// <summary>UC 3.1 step 11 / UC 3.2 step 10: sends the next queued email; a failed send stays queued with a backoff.</summary>
    public async Task<bool> DeliverQueuedEmail(IEmailService email, CancellationToken ct)
    {
        if (!email.IsConfigured)
            return false;
        // One message per call keeps the recruitment lock's SMTP workload bounded.
        await repo.RunMailDeliveryAsync(async () =>
        {
            var mail = await repo.GetNextQueuedEmailAsync(DateTime.UtcNow, SetupSubject, ct);
            if (mail == null)
                return;
            var item = mail.Email;
            try
            {
                var body = mail.Body ?? throw new InvalidOperationException("The queued email can no longer be read.");
                if (body.StartsWith("SETUP:", StringComparison.Ordinal))
                    body = $"Email đăng nhập của bạn là {item.Recipient}. Vui lòng thiết lập mật khẩu huấn luyện viên trong vòng 48 giờ: " +
                        (setupLinks ?? throw new InvalidOperationException("Configure the trainer password setup page URL.")).Build(body[6..]);
                await email.SendAsync(item.Recipient, item.Subject == SetupSubject ? "Thiết lập tài khoản huấn luyện viên" : item.Subject, body, ct);
                item.SentAt = DateTime.UtcNow;
                item.ProtectedBody = string.Empty;
                // GBR-04: a setup link expires 48 hours after it is sent.
                if (item.Subject == SetupSubject && mail.Application.HiredUserId is { } hiredId &&
                    await repo.GetUserAsync(hiredId, ct) is { } hired)
                    hired.PasswordSetupExpiresAt = item.SentAt.Value.AddHours(48);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                item.Attempts++;
                item.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(1440, Math.Pow(2, Math.Min(item.Attempts, 10))));
            }
            repo.UpdateEmailQueue(mail.Application, mail.Queue);
        }, ct);
        return true;
    }
}
