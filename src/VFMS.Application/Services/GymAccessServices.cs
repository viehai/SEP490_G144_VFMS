using System.Globalization;
using System.Security.Cryptography;
using VFMS.Application.Interfaces;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>Which membership lets a member in today. Shared by gym check-in and yoga booking.</summary>
internal static class MembershipRules
{
    /// <summary>
    /// The usable membership with the latest end date. Scheduled counts once its start date has come, because the
    /// membership worker only flips Scheduled to Active on its next run; Active past its end date is expired (GBR-89).
    /// </summary>
    public static MemberMembership? Usable(IEnumerable<MemberMembership> memberships, DateOnly today, ComponentType? type = null) =>
        memberships
            .Where(m => m.MembershipStatus is MembershipStatus.Active or MembershipStatus.Scheduled &&
                        m.StartDate <= today && m.EndDate >= today &&
                        (type is null || m.PackageComponent.ComponentType == type))
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefault();

    /// <summary>Active, None, Expired, or the status of the latest membership (Frozen, Suspended, ...).</summary>
    public static string Status(IReadOnlyCollection<MemberMembership> memberships, DateOnly today)
    {
        var latest = memberships.MaxBy(m => m.EndDate);
        return Usable(memberships, today) is not null ? "Active"
            : latest is null ? "None"
            : latest.MembershipStatus == MembershipStatus.Active && latest.EndDate < today ? "Expired"
            : latest.MembershipStatus.ToString();
    }
}

/// <summary>Messages and codes shared by the kiosk and the front desk (SRS MSG-35..38).</summary>
internal static class GymAccessMessages
{
    public const string Denied =
        "Access denied: No active membership found or biometric verification failed. Please contact the front desk.";   // MSG-36
    public const string DeniedCode = "ACCESS_DENIED";

    /// <summary>MSG-36 with the membership status, as UC "Check In" step 3 asks.</summary>
    public static string DeniedWithStatus(string status) => $"{Denied} Membership status: {status}.";
    public const string PassbackCode = "ANTI_PASSBACK";

    public static string Welcome(string name, DateOnly validThrough) =>
        string.Create(CultureInfo.InvariantCulture, $"Welcome back, {name}! Access granted. Valid through {validThrough:dd/MM/yyyy}.");   // MSG-35

    public static string Passback(int minutes) =>
        $"Access denied: Entry recorded recently. Anti-passback cooldown active for {minutes} minutes.";   // MSG-37
}

public sealed class MemberQrService : IMemberQrService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ICheckInRepository _repository;
    private readonly TimeProvider _clock;

    public MemberQrService(ICheckInRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

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
            ExpiresAt = now + Lifetime
        };
        _repository.Add(code);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<MemberQrResponse>.Success(new MemberQrResponse(code.QRToken, code.ExpiresAt), 201);
    }

    public async Task<string?> RedeemAsync(string token, CancellationToken cancellationToken = default)
    {
        var code = (await _repository.GetActiveQrCodesAsync(null, token, cancellationToken)).FirstOrDefault();
        if (code is null || code.ExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            return null;
        code.IsActive = false;   // single use: saved together with the caller's change
        return code.UserId;
    }
}

/// <summary>
/// UC-44 Self Check-In (kiosk, FaceID or QR) and the shared entry rules: Active membership (GBR-89),
/// anti-passback (GBR-90), one open visit at a time, and PT auto-completion (GBR-92).
/// </summary>
public sealed class CheckInService : ICheckInService
{
    /// <summary>Euclidean distance between face-api descriptors. Stricter than face-api's 0.6 default: a false match opens the gate.</summary>
    public const double FaceMatchThreshold = 0.5;
    public static readonly TimeSpan AntiPassback = TimeSpan.FromMinutes(15);
    /// <summary>JOB-04: a visit still open after 6 hours is treated as closed.</summary>
    public static readonly TimeSpan MaxVisit = TimeSpan.FromHours(6);

    private readonly ICheckInRepository _repository;
    private readonly IFaceTemplateProtector _protector;
    private readonly IMemberQrService _qr;
    private readonly TimeProvider _clock;

    public CheckInService(ICheckInRepository repository, IFaceTemplateProtector protector, IMemberQrService qr, TimeProvider clock)
    {
        _repository = repository;
        _protector = protector;
        _qr = qr;
        _clock = clock;
    }

    public async Task<Result<CheckInResultResponse>> KioskCheckInAsync(KioskScanRequest request, string? selfUserId = null, CancellationToken cancellationToken = default)
    {
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null || (selfUserId is not null && userId != selfUserId))
            return Result<CheckInResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
        return await CheckInMemberAsync(userId, method, null, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<FrontDeskMemberResponse>>> SearchMembersAsync(string? search, CancellationToken cancellationToken = default)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
            return Result<IReadOnlyList<FrontDeskMemberResponse>>.Success([]);
        var today = YogaTime.Today(_clock);
        var rows = await _repository.SearchMembersAsync(term, cancellationToken);
        if (rows.Count == 0)
            return Result<IReadOnlyList<FrontDeskMemberResponse>>.Failure("No matching records found.", "NOT_FOUND", 404);   // MSG-01
        return Result<IReadOnlyList<FrontDeskMemberResponse>>.Success(rows.Select(r =>
        {
            var usable = MembershipRules.Usable(r.Memberships, today);
            return new FrontDeskMemberResponse(r.User.UserId, r.User.FullName, r.User.PhoneNumber,
                MembershipRules.Status(r.Memberships, today), (usable ?? r.Memberships.MaxBy(m => m.EndDate))?.EndDate, r.IsCheckedIn);
        }).ToList());
    }

    /// <summary>UC-46: same entry rules as the kiosk, recorded as Manual with the staff member's ID; no PT auto-complete.</summary>
    public Task<Result<CheckInResultResponse>> ManualCheckInAsync(string memberUserId, string staffUserId, CancellationToken cancellationToken = default) =>
        CheckInMemberAsync(memberUserId, CheckInMethod.ManualStaff, staffUserId, cancellationToken);

    public async Task<Result<CheckOutResultResponse>> KioskCheckOutAsync(KioskScanRequest request, string? selfUserId = null, CancellationToken cancellationToken = default)
    {
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null || (selfUserId is not null && userId != selfUserId))
            return Result<CheckOutResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
        var open = await _repository.GetOpenCheckInAsync(userId, cancellationToken);
        return open is null   // GBR-91: the member goes to the front desk (UC-47)
            ? Result<CheckOutResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403)
            : await CheckOutAsync(open, method, null, cancellationToken);
    }

    public async Task<Result<KioskScanResultResponse>> KioskScanAsync(KioskScanRequest request, string? selfUserId = null, CancellationToken cancellationToken = default)
    {
        // Identify once: a QR token is single-use, so the in/out decision must not redeem it twice.
        var (userId, method) = await IdentifyAsync(request, cancellationToken);
        if (userId is null || (selfUserId is not null && userId != selfUserId))
            return Result<KioskScanResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);

        if (await _repository.GetOpenCheckInAsync(userId, cancellationToken) is { } open)
        {
            var outcome = await CheckOutAsync(open, method, null, cancellationToken);
            return outcome.IsSuccess
                ? Result<KioskScanResultResponse>.Success(new KioskScanResultResponse("CheckOut", null, outcome.Data))
                : Result<KioskScanResultResponse>.Failure(outcome.ErrorMessage!, outcome.ErrorCode!, outcome.StatusCode);
        }
        var entry = await CheckInMemberAsync(userId, method, null, cancellationToken);
        return entry.IsSuccess
            ? Result<KioskScanResultResponse>.Success(new KioskScanResultResponse("CheckIn", entry.Data, null), entry.StatusCode)
            : Result<KioskScanResultResponse>.Failure(entry.ErrorMessage!, entry.ErrorCode!, entry.StatusCode);
    }

    /// <summary>UC-47: Staff close an open visit from Active Check-ins, recorded as Manual with their ID.</summary>
    public async Task<Result<CheckOutResultResponse>> ManualCheckOutAsync(int checkInId, string staffUserId, CancellationToken cancellationToken = default)
    {
        var open = await _repository.GetCheckInAsync(checkInId, cancellationToken);
        return open is not { Status: CheckInStatus.Active }   // GBR-91
            ? Result<CheckOutResultResponse>.Failure(GymAccessMessages.Denied, "NO_OPEN_CHECKIN", 404)
            : await CheckOutAsync(open, CheckInMethod.ManualStaff, staffUserId, cancellationToken);
    }

    /// <summary>Closes an open visit and saves (GBR-91). Occupancy is the count of open visits, so it drops here.</summary>
    private async Task<Result<CheckOutResultResponse>> CheckOutAsync(
        CheckIn open, CheckInMethod method, string? staffUserId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        open.CheckoutTime = now;
        open.CheckoutMethod = method;
        open.CheckedOutByUserId = staffUserId;
        open.Status = CheckInStatus.CheckedOut;
        await _repository.SaveChangesAsync(cancellationToken);
        var name = (await _repository.GetUserAsync(open.MemberUserId, cancellationToken))?.FullName ?? string.Empty;
        return Result<CheckOutResultResponse>.Success(new CheckOutResultResponse(
            open.Id, open.MemberUserId, name, open.CheckinTime, now, (int)(now - open.CheckinTime).TotalMinutes));
    }

    /// <summary>The member behind a kiosk scan, or null (MSG-36). A matched face gets LastUsedAt; a QR token is used up.</summary>
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

    internal async Task<Result<CheckInResultResponse>> CheckInMemberAsync(
        string userId, CheckInMethod method, string? staffUserId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var localNow = YogaTime.LocalNow(_clock);
        var today = DateOnly.FromDateTime(localNow);
        var user = await _repository.GetUserAsync(userId, cancellationToken);
        var memberships = await _repository.GetMembershipsAsync(userId, cancellationToken);
        var membership = MembershipRules.Usable(memberships, today);
        if (user is null)
            return Result<CheckInResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
        if (membership is null)
            return Result<CheckInResultResponse>.Failure(
                GymAccessMessages.DeniedWithStatus(MembershipRules.Status(memberships, today)), GymAccessMessages.DeniedCode, 403);
        if (!InAccessWindow(membership.PackageComponent, TimeOnly.FromDateTime(localNow)))
            return Result<CheckInResultResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);

        if (await _repository.GetLastCheckInTimeAsync(userId, cancellationToken) is { } last && now - last < AntiPassback)
        {
            var minutes = (int)Math.Ceiling((AntiPassback - (now - last)).TotalMinutes);
            return Result<CheckInResultResponse>.Failure(GymAccessMessages.Passback(minutes), GymAccessMessages.PassbackCode, 409);
        }

        if (await _repository.GetOpenCheckInAsync(userId, cancellationToken) is { } open)
        {
            if (now - open.CheckinTime < MaxVisit)
                return Result<CheckInResultResponse>.Failure(
                    "This member is already checked in. Check out first.", "ALREADY_CHECKED_IN", 409);
            // shortcut: closes only this member's stale visit; others stay open until JOB-04 exists and inflate occupancy.
            open.Status = CheckInStatus.AutoClosed;
            open.CheckoutTime = open.CheckinTime + MaxVisit;
        }

        var checkIn = new CheckIn
        {
            MemberUserId = userId,
            MemberMembershipId = membership.Id,
            CheckinTime = now,
            CheckinMethod = method,
            HandledByUserId = staffUserId,
            Status = CheckInStatus.Active
        };
        _repository.Add(checkIn);

        int? ptSessionId = null;
        if (staffUserId is null)   // GBR-92 only for self check-in
        {
            var due = (await _repository.GetScheduledPtSessionsAsync(userId, today, cancellationToken))
                .FirstOrDefault(s => now >= YogaTime.ToUtc(s.SessionDate, s.StartTime) - TimeSpan.FromMinutes(30) &&
                                     now <= YogaTime.ToUtc(s.SessionDate, s.StartTime) + TimeSpan.FromMinutes(15));
            if (due is not null)
            {
                due.SessionStatus = PTSessionStatus.Completed;
                if (due.PTPurchase.RemainingSessions > 0)
                    due.PTPurchase.RemainingSessions--;
                ptSessionId = due.Id;
            }
        }

        await _repository.SaveChangesAsync(cancellationToken);   // one save: check-in, stale close, PT, QR use, face LastUsedAt
        return Result<CheckInResultResponse>.Success(new CheckInResultResponse(
            checkIn.Id, userId, user.FullName, membership.EndDate, now,
            GymAccessMessages.Welcome(user.FullName, membership.EndDate), ptSessionId), 201);
    }

    private static bool InAccessWindow(PackageComponent component, TimeOnly time) =>
        component.AccessStartTime is not { } start || component.AccessEndTime is not { } end || (time >= start && time <= end);

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
