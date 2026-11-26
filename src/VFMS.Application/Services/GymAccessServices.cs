using System.Globalization;
using System.Security.Cryptography;
using VFMS.Application.Interfaces;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>Which membership lets a member in today. Shared by gym check-in and yoga booking.</summary>
public static class MembershipRules
{
    /// <summary>
    /// The usable membership with the latest end date. Scheduled counts once its start date has come, because the
    /// membership worker only flips Scheduled to Active on its next run; Active past its end date is expired (GBR-89).
    /// </summary>
    public static MemberMembership? Usable(IEnumerable<MemberMembership> memberships, DateOnly today, ComponentType? type = null) =>
        memberships
            .Where(m => StatusOn(m.MembershipStatus, m.StartDate, m.EndDate, today) == MembershipStatus.Active &&
                        (type is null || m.PackageComponent.ComponentType == type))
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefault();

    /// <summary>
    /// TC-YEXP (option B): a yoga booking counts on a day only while the member has a usable yoga plan that covers the
    /// class level on that day. Otherwise the booking is on hold: the mat stays theirs, but they can't attend until they buy a new plan.
    /// </summary>
    public static bool CoversYogaClass(IEnumerable<MemberMembership> memberships, string? classLevel, DateOnly day) =>
        Usable(memberships.Where(m => VFMS.Contracts.Yoga.Validators.YogaRules.PlanCovers(m.PackageComponent.YogaDifficultyLevel, classLevel)),
            day, ComponentType.YOGA) is not null;

    public static MembershipStatus StatusOn(MembershipStatus status, DateOnly start, DateOnly end, DateOnly today)
    {
        if (status is not (MembershipStatus.Active or MembershipStatus.Scheduled)) return status;
        if (start > today) return MembershipStatus.Scheduled;
        return end < today ? MembershipStatus.Expired : MembershipStatus.Active;
    }
}

/// <summary>Messages and codes shared by the kiosk and the front desk (SRS MSG-35..38).</summary>
internal static class GymAccessMessages
{
    public const string Denied =
        "Từ chối truy cập: không tìm thấy gói hội viên còn hiệu lực hoặc không xác thực được khuôn mặt/mã QR. Vui lòng liên hệ quầy lễ tân.";   // MSG-36
    public const string NoOpenVisit = "Hội viên chưa có lượt check-in đang mở để check-out.";
    public const string AccountLocked = "Tài khoản hội viên đang bị khóa. Vui lòng liên hệ lễ tân.";
    public const string AccountLockedCode = "MEMBER_ACCOUNT_LOCKED";
    public const string DeniedCode = "ACCESS_DENIED";
    /// <summary>Kiosk scan matched no member (face not enrolled, QR token expired or replaced).</summary>
    public const string NotRecognisedCode = "SCAN_NOT_RECOGNISED";

    /// <summary>MSG-36 with the membership status, as UC "Check In" step 3 asks.</summary>
    public static string DeniedWithStatus(string status) => $"{Denied} Trạng thái gói: {StatusLabel(status)}.";

    /// <summary>Vietnamese label of a MembershipRules status (the status itself stays an English code for the client).</summary>
    public static string StatusLabel(string status) => status switch
    {
        "Active" => "Còn hiệu lực",
        "Frozen" => "Đang bảo lưu",
        "Expired" => "Hết hạn",
        "Scheduled" => "Chưa bắt đầu",
        "Suspended" => "Tạm ngưng",
        "Cancelled" => "Đã hủy",
        "None" => "Chưa có gói",
        "AwaitingActivation" => "Chờ kích hoạt",
        "PendingPayment" => "Chờ thanh toán",
        "Completed" => "Đã hoàn thành",
        _ => status
    };

    public static string Welcome(string name, DateOnly validThrough) =>
        string.Create(CultureInfo.InvariantCulture, $"Chào mừng trở lại, {name}! Mời bạn vào. Gói còn hiệu lực đến {validThrough:dd/MM/yyyy}.");   // MSG-35

}

public sealed class MemberQrService : IMemberQrService
{
    private readonly ICheckInRepository _repository;
    private readonly TimeProvider _clock;

    public MemberQrService(ICheckInRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    /// <summary>A code lasts until the gym day ends (midnight gym time), so check-in, check-out and yoga all use one code.</summary>
    public DateTime EndOfGymDayUtc() => YogaTime.ToUtc(YogaTime.Today(_clock).AddDays(1), TimeOnly.MinValue);

    public async Task<Result<MemberQrResponse>> IssueAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var old in await _repository.GetActiveQrCodesAsync(userId, null, cancellationToken))
            old.IsActive = false;   // one live code per member, so an old screenshot stops working
        var code = new QRCode
        {
            UserId = userId,
            QRToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            GeneratedAt = now,
            ExpiresAt = EndOfGymDayUtc()
        };
        _repository.Add(code);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<MemberQrResponse>.Success(new MemberQrResponse(code.QRToken, code.ExpiresAt), 201);
    }

    public async Task<Result<MemberQrResponse>> GetOrIssueAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var live = (await _repository.GetActiveQrCodesAsync(userId, null, cancellationToken))
            .Where(code => code.ExpiresAt > now).MaxBy(code => code.GeneratedAt);
        return live is null
            ? await IssueAsync(userId, cancellationToken)
            : Result<MemberQrResponse>.Success(new MemberQrResponse(live.QRToken, live.ExpiresAt));
    }

    public async Task<string?> RedeemAsync(string token, CancellationToken cancellationToken = default)
    {
        var code = (await _repository.GetActiveQrCodesAsync(null, token, cancellationToken)).FirstOrDefault();
        return code is null || code.ExpiresAt <= _clock.GetUtcNow().UtcDateTime ? null : code.UserId;
    }
}

/// <summary>
/// UC-44 Self Check-In (kiosk, FaceID or QR) and the shared entry rules: Active membership (GBR-89),
/// one open visit at a time (GBR-91).
/// </summary>
public sealed class CheckInService : ICheckInService
{
    /// <summary>Euclidean distance between face-api descriptors. Stricter than face-api's 0.6 default: a false match opens the gate.</summary>
    public const double FaceMatchThreshold = 0.5;
    /// <summary>JOB-04: a visit still open after 6 hours is treated as closed.</summary>
    public static readonly TimeSpan MaxVisit = TimeSpan.FromHours(6);

    private readonly ICheckInRepository _repository;
    private readonly IFaceTemplateProtector _protector;
    private readonly IMemberQrService _qr;
    private readonly TimeProvider _clock;
    private readonly IMembershipAccessRepository _access;

    public CheckInService(ICheckInRepository repository, IMembershipAccessRepository access, IFaceTemplateProtector protector,
        IMemberQrService qr, TimeProvider clock)
    {
        _access = access;
        _repository = repository;
        _protector = protector;
        _qr = qr;
        _clock = clock;
    }

    public async Task<Result<CheckInResultResponse>> KioskCheckInAsync(KioskScanRequest request, CancellationToken cancellationToken = default)
    {
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null)
            return Result<CheckInResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
        return await WithUnlockedMemberAsync(userId, member => CheckInMemberAsync(member, method, null, cancellationToken), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<FrontDeskMemberResponse>>> SearchMembersAsync(string? search, CancellationToken cancellationToken = default)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
            return Result<IReadOnlyList<FrontDeskMemberResponse>>.Success([]);
        var rows = await _repository.SearchMembersAsync(term, cancellationToken);
        if (rows.Count == 0)
            return Result<IReadOnlyList<FrontDeskMemberResponse>>.Failure("Không tìm thấy kết quả phù hợp.", "NOT_FOUND", 404);   // MSG-01
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now + YogaTime.GymOffset);
        var plans = (await _access.GetEntryPlansAsync(rows.Select(r => r.User.UserId).ToList(), today, now, cancellationToken))
            .ToLookup(plan => plan.MemberUserId);
        return Result<IReadOnlyList<FrontDeskMemberResponse>>.Success(rows.Select(r =>
        {
            var plan = EntryPlan.Best(plans[r.User.UserId]);
            return new FrontDeskMemberResponse(r.User.UserId, r.User.FullName, r.User.PhoneNumber,
                plan?.Status ?? "None", plan?.ValidThrough, r.IsCheckedIn, r.User.AccountStatus == AccountStatus.Locked);
        }).ToList());
    }

    /// <summary>UC-46: same entry rules as the kiosk, recorded as Manual with the staff member's ID.</summary>
    public Task<Result<CheckInResultResponse>> ManualCheckInAsync(string memberUserId, string staffUserId, CancellationToken cancellationToken = default) =>
        WithUnlockedMemberAsync(memberUserId,
            member => CheckInMemberAsync(member, CheckInMethod.ManualStaff, staffUserId, cancellationToken), cancellationToken);

    public async Task<Result<CheckOutResultResponse>> KioskCheckOutAsync(KioskScanRequest request, CancellationToken cancellationToken = default)
    {
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null)
            return Result<CheckOutResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
        return await WithUnlockedMemberAsync(userId, async member =>
        {
            var open = await _repository.GetOpenCheckInAsync(userId, cancellationToken);
            return open is null
                ? Result<CheckOutResultResponse>.Failure(GymAccessMessages.NoOpenVisit, GymAccessMessages.DeniedCode, 403)
                : await CheckOutAsync(open, member.FullName, method, null, cancellationToken);
        }, cancellationToken);
    }

    public async Task<Result<KioskScanResultResponse>> KioskScanAsync(KioskScanRequest request, CancellationToken cancellationToken = default)
    {
        // Identify once, then decide in or out.
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null)
            return Result<KioskScanResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.NotRecognisedCode, 403);

        return await WithUnlockedMemberAsync(userId, member => ScanMemberAsync(member, method, cancellationToken), cancellationToken);
    }

    private async Task<Result<KioskScanResultResponse>> ScanMemberAsync(ApplicationUser member, CheckInMethod method, CancellationToken cancellationToken)
    {
        if (await _repository.GetOpenCheckInAsync(member.UserId, cancellationToken) is { } open)
        {
            var outcome = await CheckOutAsync(open, member.FullName, method, null, cancellationToken);
            return outcome.IsSuccess
                ? Result<KioskScanResultResponse>.Success(new KioskScanResultResponse("CheckOut", null, outcome.Data))
                : Result<KioskScanResultResponse>.Failure(outcome.ErrorMessage!, outcome.ErrorCode!, outcome.StatusCode);
        }
        var entry = await CheckInMemberAsync(member, method, null, cancellationToken);
        return entry.IsSuccess
            ? Result<KioskScanResultResponse>.Success(new KioskScanResultResponse("CheckIn", entry.Data, null), entry.StatusCode)
            : Result<KioskScanResultResponse>.Failure(entry.ErrorMessage!, entry.ErrorCode!, entry.StatusCode);
    }

    /// <summary>UC-47: Staff close an open visit from Active Check-ins, recorded as Manual with their ID.</summary>
    public async Task<Result<CheckOutResultResponse>> ManualCheckOutAsync(int checkInId, string staffUserId, CancellationToken cancellationToken = default)
    {
        var visit = await _repository.GetCheckInAsync(checkInId, cancellationToken);
        if (visit is not { Status: CheckInStatus.Active }) return NoOpenVisit();
        return await WithUnlockedMemberAsync(visit.MemberUserId, async member =>
        {
            var open = await _repository.GetOpenCheckInAsync(member.UserId, cancellationToken);
            return open is not null && open.Id == checkInId
                ? await CheckOutAsync(open, member.FullName, CheckInMethod.ManualStaff, staffUserId, cancellationToken)
                : NoOpenVisit();
        }, cancellationToken);
    }

    private static Result<CheckOutResultResponse> NoOpenVisit() =>
        Result<CheckOutResultResponse>.Failure(GymAccessMessages.NoOpenVisit, "NO_OPEN_CHECKIN", 404);

    private Task<Result<T>> WithUnlockedMemberAsync<T>(string userId, Func<ApplicationUser, Task<Result<T>>> action, CancellationToken ct)
    {
        return _repository.WithMemberVisitAsync(userId, member => member switch
        {
            null => Task.FromResult(Result<T>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403)),
            { AccountStatus: AccountStatus.Locked } => Task.FromResult(Result<T>.Failure(
                GymAccessMessages.AccountLocked, GymAccessMessages.AccountLockedCode, 403)),
            _ => action(member)
        }, ct);
    }

    /// <summary>Closes an open visit and saves (GBR-91). Occupancy is the count of open visits, so it drops here.</summary>
    private async Task<Result<CheckOutResultResponse>> CheckOutAsync(
        CheckIn open, string name, CheckInMethod method, string? staffUserId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        open.CheckoutTime = now;
        open.CheckoutMethod = method;
        open.CheckedOutByUserId = staffUserId;
        open.Status = CheckInStatus.CheckedOut;
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<CheckOutResultResponse>.Success(new CheckOutResultResponse(
            open.Id, open.MemberUserId, name, open.CheckinTime, now, (int)(now - open.CheckinTime).TotalMinutes));
    }

    /// <summary>The member behind a kiosk scan, or null (MSG-36). A matched face gets LastUsedAt.</summary>
    internal async Task<(string? UserId, CheckInMethod Method)> IdentifyAsync(KioskScanRequest request, CancellationToken cancellationToken)
    {
        if (request.QrToken is { Length: > 0 } token)
            return (await _qr.RedeemAsync(token, cancellationToken), CheckInMethod.QRCode);

        // shortcut: decrypts and compares every template per scan, fine for one gym (hundreds of faces);
        // past a few thousand members move to a vector index.
        FaceData? best = null;
        var bestDistance = double.MaxValue;
        foreach (var face in await _repository.GetActiveFaceDataAsync(cancellationToken))
        {
            var distance = Distance(request.Descriptor!, _protector.Unprotect(face.FaceNetEmbedding));
            if (distance < bestDistance)
                (best, bestDistance) = (face, distance);
        }
        if (best is null || bestDistance > FaceMatchThreshold)
            return (null, CheckInMethod.FaceRecognition);
        best.LastUsedAt = _clock.GetUtcNow().UtcDateTime;
        return (best.UserId, CheckInMethod.FaceRecognition);
    }

    private async Task<Result<CheckInResultResponse>> CheckInMemberAsync(
        ApplicationUser user, CheckInMethod method, string? staffUserId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now + YogaTime.GymOffset);
        var userId = user.UserId;
        var plan = EntryPlan.Best(await _access.GetEntryPlansAsync([userId], today, now, cancellationToken));
        if (plan is not { IsActive: true, ValidThrough: { } validThrough })
            return Result<CheckInResultResponse>.Failure(
                GymAccessMessages.DeniedWithStatus(plan?.Status ?? "None"), GymAccessMessages.DeniedCode, 403);

        if (await _repository.GetOpenCheckInAsync(userId, cancellationToken) is { } open)
        {
            if (now - open.CheckinTime < MaxVisit)
                return Result<CheckInResultResponse>.Failure(
                    "Hội viên này đang ở trong phòng tập. Hãy check-out trước.", "ALREADY_CHECKED_IN", 409);
            // shortcut: closes only this member's stale visit; others stay open until JOB-04 exists and inflate occupancy.
            open.Status = CheckInStatus.AutoClosed;
            open.CheckoutTime = open.CheckinTime + MaxVisit;
        }

        var checkIn = new CheckIn
        {
            MemberUserId = userId,
            MemberMembershipId = plan.MemberMembershipId,
            CheckinTime = now,
            CheckinMethod = method,
            HandledByUserId = staffUserId,
            Status = CheckInStatus.Active
        };
        _repository.Add(checkIn);

        await _repository.SaveChangesAsync(cancellationToken);   // Persist the visit and any stale visit closure together.
        return Result<CheckInResultResponse>.Success(new CheckInResultResponse(
            checkIn.Id, userId, user.FullName, validThrough, now,
            GymAccessMessages.Welcome(user.FullName, validThrough)), 201);
    }

    private static double Distance(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count)
            return double.MaxValue;
        double sum = 0;
        for (var i = 0; i < a.Count; i++)
            sum += (a[i] - b[i]) * (double)(a[i] - b[i]);
        return Math.Sqrt(sum);
    }
}
