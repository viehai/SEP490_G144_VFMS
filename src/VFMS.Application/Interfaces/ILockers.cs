using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>Data access for Locker rentals (feature 5). A "full" rental has its locker (zone, gym), replacement locker
/// (zone), pricing policy, customer and payments (order, invoice) loaded.</summary>
public interface ILockerRepository : IAuditedRepository
{
    /// <summary>Runs the work in a serializable transaction; deadlocks and unique-key races become a 409 result.
    /// The work saves its own changes.</summary>
    Task<Result<T>> RunSerializableAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default);

    // ── Users ────────────────────────────────────────────────────
    /// <summary>An active, unbanned user holding the role.</summary>
    Task<ApplicationUser?> GetActiveUserInRoleAsync(string userId, UserRole role, CancellationToken cancellationToken = default);
    /// <summary>GBR-18: the user is an active Member with a Gym Membership covering today and not frozen today.</summary>
    Task<bool> HasEligibleGymMembershipAsync(string memberId, DateOnly today, CancellationToken cancellationToken = default);
    /// <summary>Up to 25 active Members of the gym matching the name or phone, flagged eligible when they may rent now.</summary>
    Task<IReadOnlyList<LockerMember>> SearchMembersAsync(int gymId, string search, DateTime now, DateOnly today,
        CancellationToken cancellationToken = default);

    // ── Lockers and pricing ──────────────────────────────────────
    /// <summary>Active lockers of the gym with their zone, by floor and number (read only).</summary>
    Task<IReadOnlyList<Locker>> GetGymLockersAsync(int gymId, CancellationToken cancellationToken = default);
    /// <summary>A locker of the gym with its zone and gym loaded.</summary>
    Task<Locker?> GetGymLockerAsync(int lockerId, int gymId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LockerPricingPolicy>> GetActivePoliciesAsync(CancellationToken cancellationToken = default);
    Task<LockerPricingPolicy?> GetActivePolicyAsync(int id, CancellationToken cancellationToken = default);
    Task<Gym?> GetGymAsync(int gymId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LockerZone>> GetZonesAsync(int gymId, CancellationToken cancellationToken = default);
    Task<LockerZone?> GetZoneAsync(int zoneId, int gymId, CancellationToken cancellationToken = default);
    /// <summary>MSG-48: the number is used by any locker of the gym, including deactivated ones.</summary>
    Task<bool> LockerNumberExistsAsync(int gymId, string number, CancellationToken cancellationToken = default);

    // ── Rentals ──────────────────────────────────────────────────
    /// <summary>GBR-19: true when a live allocation (in use, awaiting handover or a live checkout hold) holds the locker
    /// (as locker or replacement) or belongs to the customer.</summary>
    Task<bool> HasAllocationAsync(DateTime now, int? lockerId, string? customerId, int? excludingRentalId = null,
        CancellationToken cancellationToken = default);
    /// <summary>Live allocations of the gym, newest first (read only, nothing loaded).</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetGymAllocationsAsync(int gymId, DateTime now, CancellationToken cancellationToken = default);
    /// <summary>The full live allocation that holds the locker as locker or replacement.</summary>
    Task<LockerRentalContract?> GetAllocationForLockerAsync(int lockerId, DateTime now, CancellationToken cancellationToken = default);
    /// <summary>The member's newest full live allocation past checkout (read only).</summary>
    Task<LockerRentalContract?> GetCurrentRentalAsync(string memberId, DateTime now, CancellationToken cancellationToken = default);
    /// <summary>A full rental of the gym; customerId limits it to that customer's.</summary>
    Task<LockerRentalContract?> GetRentalAsync(int id, int gymId, string? customerId = null, CancellationToken cancellationToken = default);
    /// <summary>UC registry: up to 100 full live allocations of the gym by end date (read only).</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetRegistryAsync(int gymId, DateTime now, DateOnly today, string? search,
        bool overdueOnly, CancellationToken cancellationToken = default);
    /// <summary>Full rentals whose maintenance replacement is still held after the rental expired.</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetExpiredReplacementsAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    /// <summary>Full paid rentals still awaiting key handover (GBR-106).</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetAwaitingHandoverAsync(int take, CancellationToken cancellationToken = default);
    /// <summary>GBR-20: full active monthly rentals expiring within 7 days that have no reminder yet.</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetDueRemindersAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    /// <summary>GBR-20: full monthly rentals on overdue day 5 or later that have no overdue notice yet.</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetDueOverdueNoticesAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    /// <summary>GBR-110: the Member's unpaid locker amount from cleared lockers.</summary>
    Task<decimal> GetUnpaidAmountAsync(string memberId, CancellationToken cancellationToken = default);
    /// <summary>GBR-110: full abandoned rentals of the gym with an unpaid amount (read only).</summary>
    Task<IReadOnlyList<LockerRentalContract>> GetUnpaidRentalsAsync(int gymId, string? search, CancellationToken cancellationToken = default);

    // ── Payments ─────────────────────────────────────────────────
    /// <summary>The Locker charge of an order, taken from its full rental.</summary>
    Task<PaymentTransaction?> GetChargeByOrderAsync(int orderId, CancellationToken cancellationToken = default);
    /// <summary>A Locker charge of the gym by order number with order and invoice (read only).</summary>
    Task<PaymentTransaction?> GetCheckoutAsync(string orderNumber, int gymId, string? customerId, CancellationToken cancellationToken = default);
    /// <summary>Locker charges whose hold expired unpaid, with the rental's locker and the order loaded.</summary>
    Task<IReadOnlyList<PaymentTransaction>> GetStaleChargesAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    /// <summary>GBR-109: online rental charges whose 10-minute 'Proceed to Payment' window passed unstarted.</summary>
    Task<IReadOnlyList<PaymentTransaction>> GetUnstartedChargesAsync(DateTime now, int take, CancellationToken cancellationToken = default);
    Task<bool> IsTransferRecordedAsync(PaymentTransaction payment, string reference, CancellationToken cancellationToken = default);
    void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
    void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
    void RequestRefund(PaymentTransaction payment, string reason, DateTime now);
    void ResolveRefund(PaymentTransaction payment, string actor, PaymentMethod method, string? reference, DateTime now);

    // ── Clearances (GBR-20) ──────────────────────────────────────
    Task<int?> GetOpenClearanceIdAsync(int rentalId, CancellationToken cancellationToken = default);
    /// <summary>Up to 100 unverified clearances of the gym whose rental is still open (read only).</summary>
    Task<IReadOnlyList<LockerIncidentLog>> GetOpenClearancesAsync(int gymId, int take, CancellationToken cancellationToken = default);
    Task<LockerIncidentLog?> GetClearanceAsync(int id, int gymId, CancellationToken cancellationToken = default);
}

/// <summary>Locker rental use cases (feature 5).</summary>
public interface ILockerService
{
    Task<Result<IReadOnlyList<LockerSlot>>> Grid(string actor, UserRole role, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerPolicy>>> Pricing(string actor, UserRole role, CancellationToken ct);
    Task<Result<LockerRentalView?>> MyRental(string actor, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerPolicy>>> SavePricing(string actor, LockerPricingRequest input, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerMember>>> FindMembers(string actor, string? search, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerStaffRental>>> Registry(string actor, UserRole role, string? search, bool overdueOnly, CancellationToken ct);
    Task<Result<LockerStaffRental>> Details(string actor, UserRole role, int id, CancellationToken ct);
    Task<Result<LockerOutstanding>> MyOutstanding(string actor, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerStaffRental>>> Unpaid(string actor, string? search, CancellationToken ct);
    Task<Result<LockerRentalView>> CollectOutstanding(string actor, int id, decimal amount, CancellationToken ct);
    Task<Result<LockerRulesView>> Rules(string actor, UserRole role, CancellationToken ct);

    // UC 5.7 Manage Locker Catalog & Pricing
    Task<Result<LockerCatalog>> Catalog(string actor, CancellationToken ct);
    Task<Result<LockerCatalogZone>> AddZone(string actor, LockerZoneRequest input, CancellationToken ct);
    Task<Result<LockerSlot>> AddLocker(string actor, LockerCreateRequest input, CancellationToken ct);
    Task<Result<LockerSlot>> DeactivateLocker(string actor, int lockerId, CancellationToken ct);
    Task<Result<LockerRulesView>> SaveRules(string actor, LockerRulesRequest input, CancellationToken ct);

    Task<Result<LockerCheckout>> Rent(string actor, LockerRentalRequest input, CancellationToken ct);
    Task<Result<LockerCheckout>> CounterRent(string actor, CounterLockerRentalRequest input, CancellationToken ct);
    Task<Result<LockerCheckout>> GetCheckout(string actor, UserRole role, string number, CancellationToken ct);
    Task<Result<LockerCheckout>> Proceed(string actor, string number, CancellationToken ct);
    Task<Result<LockerCheckout>> CancelCheckout(string actor, UserRole role, string number, CancellationToken ct);
    Task<Result<LockerCheckout>> ConfirmCash(string actor, string number, decimal amount, CancellationToken ct);
    Task<Result<LockerRentalView>> Handover(string actor, int id, LockerHandoverRequest request, CancellationToken ct);
    Task<Result<LockerCheckout>> Renew(string actor, UserRole role, int id, LockerRenewalRequest request, CancellationToken ct);

    Task<Result<LockerRentalView>> SelectReplacement(string actor, int id, int lockerId, CancellationToken ct);
    Task<Result<LockerRentalView>> ConfirmRelocation(string actor, int id, int lockerId, CancellationToken ct);
    Task<Result<LockerSlot>> SetMaintenance(string actor, UserRole role, int lockerId, LockerMaintenanceRequest request, CancellationToken ct);

    Task<Result<LockerSettlementQuote>> ConfirmKeyLoss(string actor, int id, CancellationToken ct);
    Task<Result<LockerSettlementQuote>> RecordReturn(string actor, int id, LockerReturnRequest request, CancellationToken ct);
    Task<Result<LockerSettlementQuote>> SettlementQuote(string actor, int id, bool damaged, CancellationToken ct);
    Task<Result<LockerCheckout>> StartSettlement(string actor, int id, LockerSettlementRequest request, CancellationToken ct);
    Task<Result<LockerRentalView>> Terminate(string actor, int id, LockerTerminationRequest request, CancellationToken ct);

    Task<Result<int>> RequestClearance(string actor, int id, LockerClearanceRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<LockerClearanceView>>> Clearances(string actor, CancellationToken ct);
    Task<Result<LockerRentalView>> VerifyClearance(string actor, int incidentId, CancellationToken ct);

    /// <summary>Background job (LockerWorker): holds, pickup deadlines, reminders and overdue notices.</summary>
    Task Expire(CancellationToken ct);
}

/// <summary>Applies a SePay transfer to a Locker order; null when the order is not a Locker order.</summary>
public interface ILockerPaymentFulfillment
{
    Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct);
}
