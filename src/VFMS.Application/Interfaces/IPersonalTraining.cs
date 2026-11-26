using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>A page of rows; Page is clamped to the pages that exist.</summary>
public sealed record PTPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page);

/// <summary>A PT freeze request with the names and held post-freeze slots shown in its list (UC 4.8, UC 4.9).</summary>
public sealed record PTFreezeRow(PTFreeze Freeze, PTPurchase Contract, string MemberName, string TrainerName,
    string? ReplacementTrainerName, IReadOnlyList<PTCalendarSlot> HeldSlots);

/// <summary>Data access for Personal Training (UC 4.1 - 4.13).</summary>
public interface IPersonalTrainingRepository : IAuditedRepository
{
    /// <summary>Runs the work in a serializable transaction; deadlocks and unique-key races become a 409 result.
    /// The work saves its own changes.</summary>
    Task<Result<T>> RunSerializableAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default);
    void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity;

    // ── Users and trainers ───────────────────────────────────────
    /// <summary>An active, unbanned user holding the role, with the Gym loaded.</summary>
    Task<ApplicationUser?> GetActiveUserInRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
    /// <summary>Active Managers of the trainer's gym.</summary>
    Task<IReadOnlyList<string>> GetGymManagerIdsAsync(string trainerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetManagerNamesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default);
    Task<int?> GetRoleIdAsync(string roleName, CancellationToken cancellationToken = default);
    Task<ApplicationUserRole?> GetRoleAssignmentAsync(string userId, int roleId, CancellationToken cancellationToken = default);
    /// <summary>True while the user teaches a Draft or Published Yoga class.</summary>
    Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The active profile of a trainer whose Trainer Role is PT.</summary>
    Task<TrainerProfile?> GetPTProfileAsync(string trainerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainerProfile>> GetNotAcceptingProfilesAsync(CancellationToken cancellationToken = default);

    // ── Schedule ─────────────────────────────────────────────────
    /// <summary>The trainer's published weekly slots.</summary>
    Task<IReadOnlyList<PTAvailability>> GetPublishedSlotsAsync(string trainerUserId, CancellationToken cancellationToken = default);
    /// <summary>Sessions that occupy time between the dates for the trainer or the member (live checkout holds included).</summary>
    Task<IReadOnlyList<PTSession>> GetOccupyingSessionsAsync(DateTime now, DateOnly from, DateOnly through,
        string? trainerUserId, string? memberUserId, int? ignoreSessionId = null, int? ignorePurchaseId = null,
        CancellationToken cancellationToken = default);
    /// <summary>Scheduled or ongoing Yoga sessions taught by the instructor or booked by the member.</summary>
    Task<IReadOnlyList<YogaClassSchedule>> GetYogaSessionsAsync(string? instructorUserId, string? memberUserId,
        DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
    /// <summary>The trainer's teaching sessions in the week, with the Member loaded.</summary>
    Task<IReadOnlyList<PTSession>> GetTeachingSessionsAsync(string trainerUserId, DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);

    // ── Packages (UC 4.3) ────────────────────────────────────────
    Task<IReadOnlyList<Package>> GetPTPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default);
    Task<Package?> GetPTPackageAsync(int id, bool activeOnly, CancellationToken cancellationToken = default);
    /// <summary>Running (active or frozen) contracts per package.</summary>
    Task<IReadOnlyDictionary<int, int>> CountRunningContractsByPackageAsync(DateOnly today, CancellationToken cancellationToken = default);
    /// <summary>True when a package is referenced by orders or memberships, including soft-deleted history.</summary>
    Task<bool> PackageHasHistoryAsync(int packageId, CancellationToken cancellationToken = default);

    // ── Contracts ────────────────────────────────────────────────
    /// <summary>The contract with its sessions, freezes, trainer (with gym) and member loaded.</summary>
    Task<PTPurchase?> GetContractAsync(int id, CancellationToken cancellationToken = default);
    Task<PTPurchase?> GetContractForSessionAsync(int sessionId, string userId, bool asTrainer, CancellationToken cancellationToken = default);
    Task<PTPurchase?> GetContractForFreezeAsync(int freezeId, CancellationToken cancellationToken = default);
    Task<string?> GetContractTrainerIdAsync(int id, string memberId, CancellationToken cancellationToken = default);
    /// <summary>The contract of an order, with its sessions loaded.</summary>
    Task<PTPurchase?> GetContractForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PTPurchase?> GetMemberContractByOrderNumberAsync(string memberId, string orderNumber, CancellationToken cancellationToken = default);
    /// <summary>UC 4.5: the user's contracts (newest first, at most 100); an unpaid checkout only while its hold is live.</summary>
    Task<IReadOnlyList<PTPurchase>> GetVisibleContractsAsync(string userId, DateTime now, int? id, CancellationToken cancellationToken = default);
    Task<PTPage<PTPurchase>> GetMemberContractPageAsync(string memberId, DateTime now, string? trainerId, bool? completed,
        int page, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>UC 4.6: currently assigned or historical contracts of the trainer, by member name.</summary>
    Task<PTPage<PTPurchase>> GetAssignedContractPageAsync(string trainerId, DateTime now, DateOnly today, string? search,
        bool historical, int page, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>GBR-14: true while the trainer has a contract still running, make-up period included.</summary>
    Task<bool> HasRunningContractAsync(string trainerUserId, DateOnly today, CancellationToken cancellationToken = default);
    /// <summary>Unpaid checkouts whose hold has expired, with sessions, payment and voucher loaded.</summary>
    Task<IReadOnlyList<PTPurchase>> GetExpiredCheckoutsAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetReviewedContractIdsAsync(string memberId, CancellationToken cancellationToken = default);
    Task<bool> HasReviewAsync(int contractId, CancellationToken cancellationToken = default);

    // ── Sessions ─────────────────────────────────────────────────
    /// <summary>Change requests and make-up proposals past their answer deadline.</summary>
    Task<IReadOnlyList<PTSession>> GetExpiredProposalsAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    /// <summary>GBR-103: earlier sessions nobody recorded, with their contract's sessions loaded.</summary>
    Task<IReadOnlyList<PTSession>> GetUnrecordedPastSessionsAsync(DateOnly today, int take, CancellationToken cancellationToken = default);
    /// <summary>The session with its contract's sessions and freezes loaded.</summary>
    Task<PTSession?> GetSessionWithContractAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>A session the Member reported as 'PT Absent' (read only).</summary>
    Task<PTSession?> GetReportedSessionAsync(int id, CancellationToken cancellationToken = default);
    Task<PTSession?> GetTrainerSessionAsync(int id, string trainerUserId, CancellationToken cancellationToken = default);
    /// <summary>UC 4.11: open or processed disputes, newest report first, with member and trainer loaded.</summary>
    Task<PTPage<PTSession>> GetDisputePageAsync(bool processed, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CheckIn>> GetCheckInsAsync(string memberId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<string?> GetFreezeHoldTrainerIdAsync(int freezeId, string memberId, CancellationToken cancellationToken = default);

    // ── Freezes (UC 4.8, UC 4.9) ─────────────────────────────────
    /// <summary>Manager view when gymId is set (pending or processed); otherwise the member's requests for one contract.</summary>
    Task<PTPage<PTFreezeRow>> GetFreezePageAsync(int? gymId, string? memberId, int? contractId, bool processed,
        int page, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>Pending requests due for a Manager reminder (24 hours old, then daily after 48 hours).</summary>
    Task<IReadOnlyList<PTFreeze>> GetFreezesDueForReminderAsync(DateTime now, int take, CancellationToken cancellationToken = default);

    // ── Payments and vouchers (UC 4.4) ───────────────────────────
    Task<IReadOnlyList<PaymentTransaction>> GetRefundsAsync(int paymentId, CancellationToken cancellationToken = default);
    Task<VoucherRedemption?> GetRedemptionForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<bool> IsTransferRecordedAsync(PaymentTransaction payment, string reference, CancellationToken cancellationToken = default);
    void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
    void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
}

/// <summary>Personal Training use cases (UC 4.1 - 4.13).</summary>
public interface IPersonalTrainingService
{
    Task<Result<IReadOnlyList<PTWeeklySlot>>> WeeklyTemplate(string trainerId, CancellationToken ct);
    Task<Result<IReadOnlyList<PTCalendarSlot>>> Calendar(string trainerId, DateOnly from, CancellationToken ct);
    Task<Result<IReadOnlyList<PTSlotInput>>> Template(string actor, CancellationToken ct);
    Task<Result<PTOperatingHours>> OperatingHours(string actor, CancellationToken ct);
    Task<Result<bool>> Publish(string actor, PTPublishRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<PTPackageResponse>>> Packages(bool all, CancellationToken ct);
    Task<Result<IReadOnlyList<PTPackageResponse>>> ManagerPackages(string actor, CancellationToken ct);
    Task<Result<bool>> SetPackageSaleStatus(string actor, int id, bool isActive, CancellationToken ct);
    Task<Result<PTPackageDeleteResponse>> DeletePackage(string actor, int id, CancellationToken ct);
    Task<Result<PTPackageResponse>> SavePackage(string actor, int? id, PTPackageRequest request, CancellationToken ct);
    Task<Result<PTBookingPreview>> Preview(string actor, PTBookingRequest request, CancellationToken ct);
    Task<Result<PTWeeklySlot[]>> BookingSlots(string actor, PTBookingSlotsRequest request, CancellationToken ct);
    Task<Result<PTCheckoutResponse>> Checkout(string actor, PTBookingRequest request, CancellationToken ct);
    Task<Result<PTCheckoutResponse>> StartPayment(string actor, string orderNumber, PTPaymentRequest request, CancellationToken ct);
    Task<Result<PTCheckoutResponse>> ConfirmCash(string staffId, string orderNumber, decimal amount, CancellationToken ct);
    Task<Result<PTCheckoutResponse>> CheckoutStatus(string actor, string orderNumber, CancellationToken ct);
    Task<Result<PTEarlyResumePreview>> PreviewEarlyResume(string actor, int id, PTEarlyResumeRequest request, CancellationToken ct);
    Task<Result<PTContractResponse>> ConfirmEarlyResume(string actor, int id, PTEarlyResumeConfirmation request, CancellationToken ct);
    Task<Result<PagedResponse<PTFreezeResponse>>> FreezePage(string actor, bool managerView, bool processed, int page, int? contractId, CancellationToken ct);
    Task<Result<PTFreezeProposal>> PreviewFreezeRequest(string actor, int id, PTFreezeRequest input, CancellationToken ct);
    Task<Result<PTFreezeResponse>> RequestFreeze(string actor, int id, PTFreezeRequest input, CancellationToken ct);
    Task<Result<PTFreezePreview>> PreviewFreeze(string actor, int id, CancellationToken ct);
    Task<Result<PTFreezeResponse>> DecideFreeze(string actor, int id, PTFreezeDecision input, CancellationToken ct);
    /// <summary>Sends a withdraw request; the Manager decides (see NewScope.txt).</summary>
    Task<Result<PTFreezeResponse>> WithdrawFreeze(string actor, int id, CancellationToken ct);
    Task<Result<IReadOnlyList<PTRescheduleSlot>>> FreezeSlotOptions(string actor, int contractId, PTFreezeSlotOptionsRequest input, CancellationToken ct);
    Task<Result<PTWithdrawPreview>> PreviewWithdraw(string actor, int id, CancellationToken ct);
    Task<Result<PTFreezeChangePreview>> PreviewFreezeChange(string actor, int id, CancellationToken ct);
    Task<Result<PTFreezeResponse>> DecideFreezeChange(string actor, int id, PTFreezeDecision input, CancellationToken ct);
    Task<Result<PTFreezeProposal>> FreezeSlots(string actor, int id, CancellationToken ct);
    Task<Result<PTFreezeResponse>> ChangeFreezeSlots(string actor, int id, PTFreezeSlotChange input, CancellationToken ct);
    Task<Result<IReadOnlyList<PTRescheduleSlot>>> RescheduleOptions(string actor, int id, DateOnly? from, CancellationToken ct);
    Task<Result<PTSessionResponse>> RequestReschedule(string actor, int id, PTRescheduleRequest request, CancellationToken ct);
    Task<Result<PTSessionResponse>> DecideReschedule(string actor, int id, PTRescheduleDecision request, CancellationToken ct);
    Task<Result<IReadOnlyList<PTTeachingSlot>>> TeachingSchedule(string actor, DateOnly from, CancellationToken ct);
    Task<Result<IReadOnlyList<PTContractResponse>>> MyContracts(string actor, int? id, CancellationToken ct);
    Task<Result<PagedResponse<PTContractResponse>>> ContractPage(string actor, int page, string? trainerId, CancellationToken ct, bool? completed = null);
    Task<Result<PagedResponse<PTAssignedMember>>> AssignedPage(string actor, string? search, bool historical, int page, CancellationToken ct);
    Task<Result<PTSessionResponse>> ProposeMakeUp(string actor, int id, PTMakeUpRequest request, CancellationToken ct);
    Task<Result<PTSessionResponse>> Propose(string actor, int id, PTProposalRequest request, CancellationToken ct);
    Task<Result<PTSessionResponse>> RequestCancellation(string actor, int id, CancellationToken ct);
    Task<Result<PTSessionResponse>> DecideProposal(string actor, int id, PTProposalDecision request, CancellationToken ct);
    Task<Result<PTSessionResponse>> DecideMakeUpRequest(string actor, int id, PTProposalDecision request, CancellationToken ct);
    Task<Result<PTSessionResponse>> RecordAttendance(string actor, int id, bool attended, CancellationToken ct);
    Task<Result<PTSessionResponse>> ReportTrainerAbsence(string actor, int id, PTTrainerAbsenceReportRequest request, CancellationToken ct);
    Task<Result<PagedResponse<PTAttendanceDispute>>> AttendanceDisputes(string actor, bool processed, int page, CancellationToken ct);
    Task<Result<IReadOnlyList<PTDisputeCheckIn>>> DisputeCheckIns(string actor, int id, CancellationToken ct);
    Task<Result<PTSessionResponse>> DecideDispute(string actor, int id, PTAttendanceDisputeDecision request, CancellationToken ct);
    Task<Result<PTSessionResponse>> Note(string actor, int id, PTNoteRequest request, CancellationToken ct);
    Task<Result<int>> Review(string actor, int id, PTReviewRequest request, CancellationToken ct);

    // Background jobs (PersonalTrainingWorker)
    Task<Result<int>> RemindPendingFreezes(CancellationToken ct);
    Task<Result<int>> ProcessAttendance(CancellationToken ct);
    Task ExpireInactiveTrainers(DateOnly today, CancellationToken ct);
    Task ExpirePending(CancellationToken ct);
}
