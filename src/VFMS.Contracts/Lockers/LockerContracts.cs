using System.Text.Json.Serialization;
using VFMS.Contracts.Memberships;

namespace VFMS.Contracts.Lockers;

public sealed record LockerSlot(int Id, string Number, string Zone, int Floor, string Status);
public sealed record LockerPolicy(int Id, string Name, string Type, int Months, decimal RentalFee,
    decimal Deposit, decimal OverdueDailyFee);
// UC 5.7: one Key Deposit applies to every rental type; every amount is a whole VND amount greater than 0.
public sealed record LockerPricingRequest(decimal DailyFee, [property: JsonRequired] decimal KeyDeposit,
    decimal OneMonth, decimal ThreeMonths, decimal SixMonths, decimal TwelveMonths,
    [property: JsonRequired] decimal OverdueDailyFee);
public sealed record LockerRentalRequest(int LockerId, int PolicyId);
public sealed record CounterLockerRentalRequest(string MemberId, int LockerId,
    int PolicyId, string PaymentMethod);
public sealed record LockerHandoverRequest(bool TermsAccepted, bool IdentityVerified, decimal? CashAmount = null);
public sealed record LockerCashRequest(decimal Amount);
public sealed record LockerRenewalRequest(int PolicyId, string? PaymentMethod);
// ProceedBy: the Member must click 'Proceed to Payment' before this time (GBR-109); null once payment started or not applicable.
public sealed record LockerCheckout(int RentalId, string OrderNumber, string Status, decimal AmountDue,
    DateTime ExpiresAt, VietQrPaymentResponse? VietQr, string? InvoiceNumber,
    decimal RefundAmount, string? RefundReason, DateTime? RefundedAt, DateTime HoldExpiresAt = default,
    DateTime? ProceedBy = null);
public sealed record LockerRentalView(int Id, string ContractNumber, LockerSlot Locker, string Type, string Status,
    decimal RentalFee, decimal DepositHeld, DateTime? HandedOverAt, DateTime? ExpiresAt, DateTime? CollectBy,
    int OverdueDays, decimal OverdueFees, DateTime? ClearanceAfter, bool CanRenew, string? PickupInstructions,
    string? InvoiceNumber, decimal? OutstandingAmount, bool RenewalDue, int TermMonths,
    DateTime? SettledAt, decimal RefundedDeposit, string? SettlementInvoiceNumber, int? ReplacementLockerId = null,
    DateOnly? StartDate = null, DateOnly? LastRentalDate = null,
    bool DailyKeyOutstanding = false, DateTime? ReturnedAt = null,
    DateTime? ReminderSentAt = null, DateTime? OverdueNoticeSentAt = null, DateTime? OutstandingPaidAt = null);
public sealed record LockerRelocationRequest(int LockerId);
public sealed record LockerStaffRental(LockerRentalView Rental, string MemberId, string MemberName, string Phone);
public sealed record LockerMember(string Id, string Name, string Phone, bool Eligible);
public sealed record LockerOutstanding(decimal Amount);
public sealed record LockerReturnRequest(bool LockerEmpty, bool KeyLostOrDamaged);
public sealed record LockerSettlementRequest(bool KeyLostOrDamaged, string? PaymentMethod);
public sealed record LockerSettlementQuote(decimal OverdueFees, decimal DepositHeld,
    decimal RefundAmount, decimal AdditionalAmountDue, decimal ForfeitedDeposit = 0, DateTime? ReturnedAt = null);
public sealed record LockerTerminationRequest(bool KeyLostOrDamaged, bool LockerEmpty, bool LockSafe,
    string? SettlementOrderNumber, string? RefundMethod,
    string? RefundReference);
public sealed record LockerInventoryItem(string Name, int Quantity);
public sealed record LockerClearanceRequest(LockerInventoryItem[] Inventory, bool NoBelongings);
public sealed record LockerClearanceView(int Id, int RentalId, string? StaffId, DateTime ReportedAt,
    IReadOnlyList<LockerInventoryItem> Inventory);
public sealed record LockerMaintenanceRequest(bool Maintenance, bool SafeInspection);

// UC 5.7 Manage Locker Catalog & Pricing
public sealed record LockerCatalogZone(int Id, string Name, int Floor, IReadOnlyList<LockerSlot> Lockers);
public sealed record LockerCatalog(IReadOnlyList<LockerCatalogZone> Zones, IReadOnlyList<LockerPolicy> Prices, string Rules);
public sealed record LockerRulesView(string Text);
public sealed record LockerZoneRequest(string Name, int Floor);
public sealed record LockerCreateRequest(int ZoneId, string Number);
public sealed record LockerRulesRequest(string? Text);
