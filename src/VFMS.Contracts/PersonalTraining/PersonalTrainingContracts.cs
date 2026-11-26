using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Contracts.PersonalTraining;

public sealed record PTSlotInput(DayOfWeek DayOfWeek, TimeOnly StartTime);
public sealed record PTWeeklySlot(int TemplateId, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime,
    string? BlockedReason = null);
public sealed record PTTeachingSlot(int Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string MemberName, string Status, int ContractId = 0,
    DateOnly? ProposedDate = null, TimeOnly? ProposedStartTime = null, TimeOnly? ProposedEndTime = null);
public sealed record PTPublishRequest(PTSlotInput[] Slots);
public sealed record PTOperatingHours(TimeOnly? OpenTime, TimeOnly? CloseTime);
public sealed record PTCalendarSlot(int TemplateId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Status);
public sealed record PTPackageRequest(string Name,
    string? Description, int Credits, decimal Price,
    string TrainerLevel, int FreezeMinimumDays, int FreezeMaxTotalDays, bool IsActive = true);
public sealed record PTPackageResponse(int Id, string Code, string Name, string? Description, int Credits,
    decimal Price, string TrainerLevel, int FreezeMinimumDays, int FreezeMaxTotalDays, bool IsActive,
    int? SubscriberCount = null);
public sealed record PTPackageStatusRequest(bool? IsActive);
public sealed record PTPackageDeleteResponse(bool KeptHistory);
public sealed record PTBookingRequest(string TrainerId, int PackageId,
    DateOnly StartDate, int[] TemplateIds);
public sealed record PTBookingSlotsRequest(string TrainerId, int PackageId,
    DateOnly StartDate, int[] TemplateIds);
public sealed record PTPaymentRequest(string? VoucherCode = null, string? PaymentMethod = null);
public sealed record PTCashConfirmationRequest(decimal AmountReceived);
public sealed record PTBookingPreview(int PackageId, string TrainerId, DateOnly ExpiresOn,
    decimal Price, IReadOnlyList<PTCalendarSlot> Sessions)
{
    /// <summary>GBR-12: busy dates that were skipped; their sessions moved to the end of the schedule.</summary>
    public IReadOnlyList<PTCalendarSlot> Skipped { get; init; } = [];
    /// <summary>Dates in the first week that are less than 24 hours away; that weekday starts one week later.</summary>
    public IReadOnlyList<PTCalendarSlot> Deferred { get; init; } = [];
}
public sealed record PTCheckoutResponse(int ContractId, string OrderNumber, string Status, decimal AmountDue,
    DateTime ExpiresAt, VietQrPaymentResponse? VietQr, string? InvoiceNumber, DateTime HoldExpiresAt = default,
    IReadOnlyList<CheckoutRefundView>? Refunds = null,
    DateTime? ProceedBefore = null, bool PayAtCounter = false);
public sealed record PTProposalRequest(DateOnly Date, TimeOnly StartTime, string Type);
public sealed record PTProposalResponse(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string Type, DateTime ExpiresAt);
public sealed record PTProposalDecision(bool Accept, DateTime ProposalExpiresAt);
public sealed record PTRescheduleRequest(DateOnly Date, TimeOnly StartTime);
public sealed record PTRescheduleDecision(bool Accept, DateOnly Date, TimeOnly StartTime, DateTime ProposalExpiresAt);
public sealed record PTRescheduleSlot(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Status, string? Reason = null);
public sealed record PTMakeUpRequest(DateOnly Date, TimeOnly StartTime);
public sealed record PTNoteRequest(string Note);
/// <summary>Member 'PT Absent' report on the session day (GBR-103); the note is optional.</summary>
public sealed record PTTrainerAbsenceReportRequest(string? Reason = null);
public static class PTDisputeDecisions
{
    public const string SessionHeld = "SessionHeld";
    public const string PTAbsent = "PTAbsent";
    public const string MemberAbsent = "MemberAbsent";
    public static readonly string[] All = [SessionHeld, PTAbsent, MemberAbsent];
}
/// <summary>UC 4.11 Manager decision: SessionHeld, PTAbsent or MemberAbsent.</summary>
public sealed record PTAttendanceDisputeDecision(string Decision, string Note);
public sealed record PTAttendanceDispute(int SessionId, int ContractId, string MemberName, string TrainerName,
    DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string? MemberNote, DateTime ReportedAt, string Status,
    string? Decision, string? ReviewNote, string? ReviewerName = null,
    bool? TrainerRecordedPresent = null, DateTime? TrainerRecordedAt = null);
/// <summary>UC 4.11 step 3: one gym visit of the Member on the disputed session's day (times in UTC).</summary>
public sealed record PTDisputeCheckIn(DateTime CheckinTime, string CheckinMethod, string? CheckedInBy,
    DateTime? CheckoutTime, string? CheckoutMethod, string? CheckedOutBy, string Status,
    bool InCheckInWindow, bool OverlapsSession);
public sealed record PTSessionResponse(int Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string Status, int CreditUnits, string? Note, DateTime? NoteUpdatedAt, PTProposalResponse? Proposal,
    DateTime? TrainerAbsenceReportedAt = null, bool? TrainerAbsenceApproved = null,
    string? TrainerAbsenceReviewNote = null)
{
    /// <summary>Cancelled without penalty and not rebooked yet: a make-up can be proposed from it (GBR-14).</summary>
    public bool IsMakeUpUnit { get; init; }
    /// <summary>'Present' set by the Member's self check-in; the PT has not recorded attendance (UC 4.6 step 7).</summary>
    public bool CompletedByCheckIn { get; init; }
    /// <summary>A make-up unit from a 'PT Absent' report that waits for the PT: usable from this time (UTC, GBR-103).</summary>
    public DateTime? MakeUpAvailableAt { get; init; }
}
public sealed record PTContractResponse(int Id, string TrainerId, string MemberId, string? PackageName,
    string Status, DateOnly StartDate, DateOnly EndDate, int TotalCredits, int UsedCredits,
    int ReservedCredits, int AvailableCredits, int RemainingCredits,
    int FreezeMinimumDays, int FreezeMaxTotalDays, int RemainingFreezeDays,
    IReadOnlyList<PTSessionResponse> Sessions, string? OrderNumber = null, DateTime? PaymentExpiresAt = null,
    string? TrainerName = null, bool HasPendingOrApprovedFreeze = false, string? MemberName = null)
{
    /// <summary>Cancelled sessions returned without penalty and not yet rebooked (GBR-14).</summary>
    public int MakeUpUnits { get; init; }
    /// <summary>Contract end date + 14 days; shown only when make-up units exist.</summary>
    public DateOnly? MakeUpDeadline { get; init; }
    public bool FreezeUsed { get; init; }
    /// <summary>Open one-week window to choose another trainer (GBR-104).</summary>
    /// <summary>UC 4.10: the Member already reviewed this contract.</summary>
    public bool HasReview { get; init; }
    /// <summary>UC 4.10: completed and its final session was held, so it can be reviewed once.</summary>
    public bool CanReview { get; init; }
    /// <summary>The package's trainer level (tier), shown in the review modal (UC 4.10 step 2).</summary>
    public string? TrainerLevel { get; init; }
    /// <summary>Gym operating hours, for choosing a make-up hour (UC 4.7).</summary>
    public TimeOnly? GymOpenTime { get; init; }
    public TimeOnly? GymCloseTime { get; init; }
}
public sealed record PTAssignedMember(int ContractId, string MemberId, string FullName, string? AvatarUrl,
    string Email, string Phone, int TotalCredits, int RemainingCredits, DateOnly ExpiresOn, DateOnly? LastTrainedOn);
/// <summary>
/// UC 4.8: Moves override the suggested post-freeze slot of a frozen session (same trainer). With a replacement
/// trainer, ReplacementTemplateIds is that trainer's weekly pattern for all remaining sessions (GBR-105).
/// </summary>
public sealed record PTFreezeRequest(DateOnly StartDate, DateOnly ResumeDate, string Reason,
    PTEarlyResumeMove[]? Moves = null, string? ReplacementTrainerId = null, int[]? ReplacementTemplateIds = null);
/// <summary>Proposed sessions after the freeze; SessionsNeedingSlot lists frozen sessions with no free slot yet.</summary>
public sealed record PTFreezeProposal(DateOnly ExpiresOn, IReadOnlyList<PTPlannedSession> Sessions,
    IReadOnlyList<int> SessionsNeedingSlot);
public sealed record PTPlannedSession(int OriginalSessionId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string TrainerId);
/// <summary>Trainer a member may pick when asking to freeze (UC 4.8).</summary>
public sealed record PTReplacementTrainer(string UserId, int ProfileId, string FullName, string Level, string Specialty);
public sealed record PTFreezeDecision(bool Approve, string? Reason);
public sealed record PTFreezeResponse(int Id, int ContractId, string MemberId, string TrainerId, DateOnly StartDate,
    DateOnly ResumeDate, string Status, string? Reason, string? ReviewNote,
    DateTime? ReviewedAt, string? MemberName = null, string? TrainerName = null, string? ReplacementTrainerId = null)
{
    /// <summary>Freeze policy saved in the contract (UC 4.9).</summary>
    public int MinimumDays { get; init; }
    public int MaximumDays { get; init; }
    /// <summary>Manager list (UC 4.9 step 2): held post-freeze slots and the replacement trainer, if any.</summary>
    public string? ReplacementTrainerName { get; init; }
    public IReadOnlyList<PTCalendarSlot> ProposedSlots { get; init; } = [];
    /// <summary>A withdraw or early-resume request waiting for (or answered by) the Manager.</summary>
    public string? ChangeRequestType { get; init; }
    public string? ChangeRequestStatus { get; init; }
    public DateTime? ChangeRequestedAt { get; init; }
    public DateOnly? ChangeResumeDate { get; init; }
    public string? ChangeReviewNote { get; init; }
    /// <summary>What the approved request did: restored sessions and sessions that could not be restored.</summary>
    public string? ChangeOutcome { get; init; }
}
/// <summary>UC 4.8 step 8.4: new slots for held sessions of a pending freeze, keyed by the frozen session id.</summary>
public sealed record PTFreezeSlotChange(PTEarlyResumeMove[] Moves);
/// <summary>The proposed post-freeze schedule for Manager review.</summary>
public sealed record PTFreezePreview(DateOnly ExpiresOn, IReadOnlyList<PTCalendarSlot> Replacements,
    string? ReplacementTrainerName = null)
{
    /// <summary>UC 4.9: the original sessions inside the freeze window, which this request moves.</summary>
    public IReadOnlyList<PTCalendarSlot> Original { get; init; } = [];
    /// <summary>UC 4.9: sessions outside the freeze window that stay as scheduled.</summary>
    public IReadOnlyList<PTCalendarSlot> Kept { get; init; } = [];
}
/// <summary>Free and busy slots of the trainer (or a replacement trainer) from a date, to pick a session's new slot.</summary>
public sealed record PTFreezeSlotOptionsRequest(DateOnly From, string? TrainerId = null);
/// <summary>One frozen session when a freeze is withdrawn: its original slot, where it is now, and whether it goes back.</summary>
public sealed record PTWithdrawItem(DateOnly OriginalDate, TimeOnly OriginalStart, DateOnly CurrentDate,
    TimeOnly CurrentStart, bool Restored, string? Reason);
/// <summary>Schedule slots carry Status Restored, Kept (could not go back) or Scheduled (unchanged).</summary>
public sealed record PTWithdrawPreview(DateOnly ExpiresOn, IReadOnlyList<PTWithdrawItem> Items,
    IReadOnlyList<PTCalendarSlot> Schedule);
/// <summary>What a pending withdraw or early-resume request would do, for the Manager.</summary>
public sealed record PTFreezeChangePreview(string Type, DateOnly ExpiresOn, PTWithdrawPreview? Withdraw,
    PTEarlyResumePreview? EarlyResume);
public sealed record PTEarlyResumeMove(int SessionId, DateOnly Date, TimeOnly StartTime);
public sealed record PTEarlyResumeRequest(DateOnly ResumeDate, PTEarlyResumeMove[]? Moves = null, int[]? TemplateIds = null);
public sealed record PTEarlyResumeConfirmation(DateOnly ResumeDate, DateOnly CurrentResumeDate,
    PTEarlyResumeMove[] Moves);
public sealed record PTEarlyResumePreview(DateOnly CurrentResumeDate, DateOnly ResumeDate, DateOnly ExpiresOn,
    int ActualFrozenDays, int RemainingFreezeDays, IReadOnlyList<PTEarlyResumeMove> Moves,
    IReadOnlyList<PTSessionResponse> Sessions, IReadOnlyList<int> SessionsNeedingNewSlot,
    IReadOnlyList<int> UnavailableOriginalSlots);
public sealed record PTReviewRequest(int Rating, string Comment);
