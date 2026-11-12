using System.ComponentModel.DataAnnotations;
using VFMS.Contracts.Memberships;

namespace VFMS.Contracts.PersonalTraining;

public sealed record PTSlotInput(DayOfWeek DayOfWeek, TimeOnly StartTime);
public sealed record PTPublishRequest([Required, MinLength(15), MaxLength(168)] PTSlotInput[] Slots);
public sealed record PTCalendarSlot(int TemplateId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Status);
public sealed record PTPackageRequest([Required, StringLength(256)] string Name,
    [StringLength(500)] string? Description, int Credits, int ValidityDays, decimal Price,
    [Required] string TrainerLevel, int FreezeMinimumDays, int FreezeMaxTotalDays, bool IsActive = true);
public sealed record PTPackageResponse(int Id, string Code, string Name, string? Description, int Credits,
    int ValidityDays, decimal Price, string TrainerLevel, int FreezeMinimumDays, int FreezeMaxTotalDays, bool IsActive);
public sealed record PTBookingRequest([Required, StringLength(450)] string TrainerId, int PackageId,
    DateOnly StartDate, [Required, MinLength(1), MaxLength(7)] int[] TemplateIds);
public sealed record PTPaymentRequest([StringLength(80)] string? VoucherCode = null);
public sealed record PTScheduleSuggestion(string TrainerId, DateOnly StartDate, int[] TemplateIds,
    IReadOnlyList<PTCalendarSlot> Sessions);
public sealed record PTBookingPreview(int PackageId, string TrainerId, DateOnly ExpiresOn,
    decimal Price, IReadOnlyList<PTCalendarSlot> Sessions, IReadOnlyList<PTScheduleSuggestion>? Suggestions = null);
public sealed record PTCheckoutResponse(int ContractId, string OrderNumber, string Status, decimal AmountDue,
    DateTime ExpiresAt, VietQrPaymentResponse? VietQr, string? InvoiceNumber);
public sealed record PTProposalRequest(DateOnly Date, TimeOnly StartTime, [Required] string Type);
public sealed record PTProposalResponse(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string Type, DateTime ExpiresAt);
public sealed record PTProposalDecision(bool Accept, DateTime ProposalExpiresAt);
public sealed record PTRescheduleRequest(DateOnly Date, TimeOnly StartTime);
public sealed record PTRescheduleDecision(bool Accept, DateOnly Date, TimeOnly StartTime, DateTime ProposalExpiresAt);
public sealed record PTRescheduleSlot(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Status);
public sealed record PTMakeUpRequest(DateOnly Date, TimeOnly StartTime);
public sealed record PTNoteRequest([Required, StringLength(2000)] string Note);
public sealed record PTSessionResponse(int Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string Status, int CreditUnits, string? Note, DateTime? NoteUpdatedAt, PTProposalResponse? Proposal);
public sealed record PTContractResponse(int Id, string TrainerId, string MemberId, string? PackageName,
    string Status, DateOnly StartDate, DateOnly EndDate, int TotalCredits, int UsedCredits,
    int ReservedCredits, int AvailableCredits, int RemainingCredits,
    int FreezeMinimumDays, int FreezeMaxTotalDays, int RemainingFreezeDays,
    IReadOnlyList<PTSessionResponse> Sessions);
public sealed record PTAssignedMember(int ContractId, string MemberId, string FullName, string? AvatarUrl,
    string Email, string Phone, int TotalCredits, int RemainingCredits, DateOnly ExpiresOn, DateOnly? LastTrainedOn);
public sealed record PTFreezeRequest(DateOnly StartDate, DateOnly ResumeDate, [Required, StringLength(1000)] string Reason);
public sealed record PTFreezeDecision(bool Approve, [StringLength(1000)] string? Reason);
public sealed record PTFreezeResponse(int Id, int ContractId, string MemberId, string TrainerId, DateOnly StartDate,
    DateOnly ResumeDate, string Status, string? Reason, string? ReviewNote,
    DateTime? ReviewedAt);
public sealed record PTFreezePreview(DateOnly ExpiresOn, IReadOnlyList<PTCalendarSlot> Replacements);
public sealed record PTReviewRequest([Range(1, 5)] int Rating, [Required, StringLength(500, MinimumLength = 10)] string Comment);
