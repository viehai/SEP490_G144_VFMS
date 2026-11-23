using VFMS.Contracts.Lockers;

namespace VFMS.Client.Services;

/// <summary>Typed calls to <c>api/lockers</c> (feature 5). Business rules stay on the server; these only carry the request.</summary>
public sealed class LockerApiClient(VfmsApiClient api)
{
    private const string Root = "api/lockers";

    // Member
    public Task<LockerSlot[]> GridAsync(CancellationToken ct = default) => api.GetAsync<LockerSlot[]>(Root, ct);
    public Task<LockerPolicy[]> PricingAsync(CancellationToken ct = default) => api.GetAsync<LockerPolicy[]>($"{Root}/pricing", ct);
    public Task<LockerRulesView> RulesAsync(CancellationToken ct = default) => api.GetAsync<LockerRulesView>($"{Root}/rules", ct);
    public Task<LockerOutstanding> OutstandingAsync(CancellationToken ct = default) =>
        api.GetAsync<LockerOutstanding>($"{Root}/my-outstanding", ct);

    /// <summary>The Member's current rental; null when there is none (the API answers 204).</summary>
    public async Task<LockerRentalView?> MyRentalAsync(CancellationToken ct = default)
    {
        try
        {
            return await api.GetAsync<LockerRentalView>($"{Root}/my-rental", ct);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NoContent || ex.ErrorCode == "EMPTY_RESPONSE")
        {
            return null;
        }
    }

    public Task<LockerCheckout> RentAsync(LockerRentalRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/rentals", request, ct);
    public Task<LockerCheckout> OrderAsync(string number, CancellationToken ct = default) =>
        api.GetAsync<LockerCheckout>($"{Root}/orders/{Uri.EscapeDataString(number)}", ct);
    public Task<LockerCheckout> ProceedAsync(string number, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/orders/{Uri.EscapeDataString(number)}/proceed", null, ct);
    public Task<LockerCheckout> CancelOrderAsync(string number, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/orders/{Uri.EscapeDataString(number)}/cancel", null, ct);
    public Task<LockerCheckout> RenewAsync(int rentalId, LockerRenewalRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/rentals/{rentalId}/renew", request, ct);
    public Task<LockerRentalView> SelectReplacementAsync(int rentalId, int lockerId, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/rentals/{rentalId}/replacement", new LockerRelocationRequest(lockerId), ct);

    // Staff (front desk)
    public Task<LockerSlot[]> DeskGridAsync(CancellationToken ct = default) => api.GetAsync<LockerSlot[]>($"{Root}/staff/grid", ct);
    public Task<LockerPolicy[]> DeskPricingAsync(CancellationToken ct = default) => api.GetAsync<LockerPolicy[]>($"{Root}/staff/pricing", ct);
    public Task<LockerRulesView> DeskRulesAsync(CancellationToken ct = default) => api.GetAsync<LockerRulesView>($"{Root}/staff/rules", ct);
    public Task<LockerMember[]> MembersAsync(string search, CancellationToken ct = default) =>
        api.GetAsync<LockerMember[]>($"{Root}/staff/members?search={Uri.EscapeDataString(search)}", ct);
    public Task<LockerStaffRental[]> RegistryAsync(string? search, bool overdueOnly, CancellationToken ct = default) =>
        api.GetAsync<LockerStaffRental[]>(
            $"{Root}/staff/rentals?search={Uri.EscapeDataString(search ?? "")}&overdueOnly={(overdueOnly ? "true" : "false")}", ct);
    public Task<LockerStaffRental[]> UnpaidAsync(string? search, CancellationToken ct = default) =>
        api.GetAsync<LockerStaffRental[]>($"{Root}/staff/unpaid?search={Uri.EscapeDataString(search ?? "")}", ct);
    public Task<LockerCheckout> CounterRentAsync(CounterLockerRentalRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/staff/rentals", request, ct);
    public Task<LockerCheckout> CounterOrderAsync(string number, CancellationToken ct = default) =>
        api.GetAsync<LockerCheckout>($"{Root}/staff/orders/{Uri.EscapeDataString(number)}", ct);
    public Task<LockerCheckout> CancelCounterOrderAsync(string number, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/staff/orders/{Uri.EscapeDataString(number)}/cancel", null, ct);
    public Task<LockerCheckout> ConfirmCashAsync(string number, decimal amount, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/staff/orders/{Uri.EscapeDataString(number)}/cash", new LockerCashRequest(amount), ct);
    public Task<LockerRentalView> HandoverAsync(int rentalId, LockerHandoverRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/staff/rentals/{rentalId}/handover", request, ct);
    public Task<LockerCheckout> DeskRenewAsync(int rentalId, LockerRenewalRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/staff/rentals/{rentalId}/renew", request, ct);
    public Task<LockerRentalView> ConfirmRelocationAsync(int rentalId, int lockerId, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/staff/rentals/{rentalId}/replacement/handover", new LockerRelocationRequest(lockerId), ct);
    public Task<LockerSettlementQuote> KeyLossAsync(int rentalId, CancellationToken ct = default) =>
        api.PostAsync<LockerSettlementQuote>($"{Root}/staff/rentals/{rentalId}/key-loss", null, ct);
    public Task<LockerSettlementQuote> ReturnAsync(int rentalId, LockerReturnRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerSettlementQuote>($"{Root}/staff/rentals/{rentalId}/return", request, ct);
    public Task<LockerSettlementQuote> SettlementQuoteAsync(int rentalId, bool keyLostOrDamaged, CancellationToken ct = default) =>
        api.GetAsync<LockerSettlementQuote>(
            $"{Root}/staff/rentals/{rentalId}/settlement?keyLostOrDamaged={(keyLostOrDamaged ? "true" : "false")}", ct);
    public Task<LockerCheckout> StartSettlementAsync(int rentalId, LockerSettlementRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCheckout>($"{Root}/staff/rentals/{rentalId}/settlement", request, ct);
    public Task<LockerRentalView> TerminateAsync(int rentalId, LockerTerminationRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/staff/rentals/{rentalId}/terminate", request, ct);
    public Task<int> RequestClearanceAsync(int rentalId, LockerClearanceRequest request, CancellationToken ct = default) =>
        api.PostAsync<int>($"{Root}/staff/rentals/{rentalId}/clearance", request, ct);
    public Task<LockerRentalView> CollectOutstandingAsync(int rentalId, decimal amount, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/staff/rentals/{rentalId}/outstanding/collect", new LockerCashRequest(amount), ct);
    public Task<LockerSlot> SetMaintenanceAsync(int lockerId, LockerMaintenanceRequest request, CancellationToken ct = default) =>
        api.PutAsync<LockerSlot>($"{Root}/staff/grid/{lockerId}/maintenance", request, ct);

    // Manager
    public Task<LockerCatalog> CatalogAsync(CancellationToken ct = default) => api.GetAsync<LockerCatalog>($"{Root}/manager/catalog", ct);
    public Task<LockerPolicy[]> SavePricingAsync(LockerPricingRequest request, CancellationToken ct = default) =>
        api.PutAsync<LockerPolicy[]>($"{Root}/manager/pricing", request, ct);
    public Task<LockerCatalogZone> AddZoneAsync(LockerZoneRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerCatalogZone>($"{Root}/manager/zones", request, ct);
    public Task<LockerSlot> AddLockerAsync(LockerCreateRequest request, CancellationToken ct = default) =>
        api.PostAsync<LockerSlot>($"{Root}/manager/lockers", request, ct);
    public Task<LockerSlot> DeactivateLockerAsync(int lockerId, CancellationToken ct = default) =>
        api.PostAsync<LockerSlot>($"{Root}/manager/lockers/{lockerId}/deactivate", null, ct);
    public Task<LockerRulesView> SaveRulesAsync(LockerRulesRequest request, CancellationToken ct = default) =>
        api.PutAsync<LockerRulesView>($"{Root}/manager/rules", request, ct);
    public Task<LockerClearanceView[]> ClearancesAsync(CancellationToken ct = default) =>
        api.GetAsync<LockerClearanceView[]>($"{Root}/manager/clearances", ct);
    public Task<LockerRentalView> VerifyClearanceAsync(int incidentId, CancellationToken ct = default) =>
        api.PostAsync<LockerRentalView>($"{Root}/manager/clearances/{incidentId}/verify", null, ct);
}
