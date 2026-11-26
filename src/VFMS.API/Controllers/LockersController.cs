using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Authorize]
[Route("api/lockers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LockersController(ILockerService service) : ControllerBase
{
    private async Task<IActionResult> AsUser<T>(Func<string, Task<Result<T>>> action) =>
        User.GetUserId() is { } actor ? this.ToActionResult(await action(actor)) : Unauthorized();

    private UserRole DeskRole => User.IsInRole(nameof(UserRole.Manager)) ? UserRole.Manager : UserRole.Staff;

    [HttpGet]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Grid(CancellationToken ct) => AsUser(actor => service.Grid(actor, UserRole.Member, ct));

    [HttpGet("pricing")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Pricing(CancellationToken ct) => AsUser(actor => service.Pricing(actor, UserRole.Member, ct));

    [HttpGet("my-rental")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> MyRental(CancellationToken ct) => AsUser(actor => service.MyRental(actor, ct));

    [HttpGet("my-rentals/history")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> MyHistory([FromQuery] int page = 1, CancellationToken ct = default) =>
        AsUser(actor => service.MyHistory(actor, page, ct));

    [HttpGet("my-rentals/{id:int}")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> MyRentalDetails(int id, CancellationToken ct) =>
        AsUser(actor => service.MyRentalDetails(actor, id, ct));

    [HttpGet("rules")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Rules(CancellationToken ct) => AsUser(actor => service.Rules(actor, UserRole.Member, ct));

    [HttpGet("my-outstanding")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> MyOutstanding(CancellationToken ct) => AsUser(actor => service.MyOutstanding(actor, ct));

    [HttpPost("rentals")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Rent(LockerRentalRequest request, CancellationToken ct) =>
        AsUser(actor => service.Rent(actor, request, ct));

    [HttpGet("orders/{number}")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Order(string number, CancellationToken ct) =>
        AsUser(actor => service.GetCheckout(actor, UserRole.Member, number, ct));

    [HttpPost("orders/{number}/proceed")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Proceed(string number, CancellationToken ct) =>
        AsUser(actor => service.Proceed(actor, number, ct));

    [HttpPost("orders/{number}/cancel")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> CancelOrder(string number, CancellationToken ct) =>
        AsUser(actor => service.CancelCheckout(actor, UserRole.Member, number, ct));

    [HttpPost("orders/{number}/payment")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ChoosePayment(string number, LockerPaymentRequest request, CancellationToken ct) =>
        AsUser(actor => service.ChoosePayment(actor, UserRole.Member, number, request, ct));

    [HttpPost("staff/orders/{number}/payment")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> ChooseCounterPayment(string number, LockerPaymentRequest request, CancellationToken ct) =>
        AsUser(actor => service.ChoosePayment(actor, UserRole.Staff, number, request, ct));

    [HttpPost("rentals/{id:int}/renew")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Renew(int id, LockerRenewalRequest request, CancellationToken ct) =>
        AsUser(actor => service.Renew(actor, UserRole.Member, id, request, ct));

    [HttpPost("rentals/{id:int}/replacement")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> SelectReplacement(int id, LockerRelocationRequest request, CancellationToken ct) =>
        AsUser(actor => service.SelectReplacement(actor, id, request.LockerId, ct));

    [HttpGet("staff/grid")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> DeskGrid(CancellationToken ct) => AsUser(actor => service.Grid(actor, DeskRole, ct));

    [HttpGet("staff/pricing")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> DeskPricing(CancellationToken ct) => AsUser(actor => service.Pricing(actor, DeskRole, ct));

    [HttpPut("manager/pricing")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> SavePricing(LockerPricingRequest request, CancellationToken ct) =>
        AsUser(actor => service.SavePricing(actor, request, ct));

    [HttpGet("staff/members")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Members([FromQuery] string? search, CancellationToken ct) =>
        AsUser(actor => service.FindMembers(actor, search, ct));

    [HttpGet("staff/rentals")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> Registry([FromQuery] string? search, [FromQuery] bool overdueOnly, CancellationToken ct) =>
        AsUser(actor => service.Registry(actor, DeskRole, search, overdueOnly, ct));

    [HttpGet("staff/rentals/{id:int}")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> RentalDetails(int id, CancellationToken ct) =>
        AsUser(actor => service.Details(actor, DeskRole, id, ct));

    [HttpPost("staff/rentals")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> CounterRent(CounterLockerRentalRequest request, CancellationToken ct) =>
        AsUser(actor => service.CounterRent(actor, request, ct));

    [HttpGet("staff/orders/{number}")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> CounterOrder(string number, CancellationToken ct) =>
        AsUser(actor => service.GetCheckout(actor, DeskRole, number, ct));

    [HttpPost("staff/orders/{number}/cancel")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> CancelCounterOrder(string number, CancellationToken ct) =>
        AsUser(actor => service.CancelCheckout(actor, UserRole.Staff, number, ct));

    [HttpPost("staff/orders/{number}/cash")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> ConfirmCash(string number, LockerCashRequest request, CancellationToken ct) =>
        AsUser(actor => service.ConfirmCash(actor, number, request.Amount, ct));

    [HttpPost("staff/rentals/{id:int}/handover")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Handover(int id, LockerHandoverRequest request, CancellationToken ct) =>
        AsUser(actor => service.Handover(actor, id, request, ct));

    [HttpPost("staff/rentals/{id:int}/renew")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> DeskRenew(int id, LockerRenewalRequest request, CancellationToken ct) =>
        AsUser(actor => service.Renew(actor, UserRole.Staff, id, request, ct));

    [HttpPost("staff/rentals/{id:int}/replacement/handover")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> ConfirmReplacement(int id, LockerRelocationRequest request, CancellationToken ct) =>
        AsUser(actor => service.ConfirmRelocation(actor, id, request.LockerId, ct));

    [HttpPost("staff/rentals/{id:int}/key-loss")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> ConfirmKeyLoss(int id, CancellationToken ct) =>
        AsUser(actor => service.ConfirmKeyLoss(actor, id, ct));

    [HttpPost("staff/rentals/{id:int}/return")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> RecordReturn(int id, LockerReturnRequest request, CancellationToken ct) =>
        AsUser(actor => service.RecordReturn(actor, id, request, ct));

    [HttpGet("staff/rentals/{id:int}/settlement")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> SettlementQuote(int id, [FromQuery] bool keyLostOrDamaged, CancellationToken ct) =>
        AsUser(actor => service.SettlementQuote(actor, id, keyLostOrDamaged, ct));

    [HttpPost("staff/rentals/{id:int}/settlement")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> StartSettlement(int id, LockerSettlementRequest request, CancellationToken ct) =>
        AsUser(actor => service.StartSettlement(actor, id, request, ct));

    [HttpPost("staff/rentals/{id:int}/terminate")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Terminate(int id, LockerTerminationRequest request, CancellationToken ct) =>
        AsUser(actor => service.Terminate(actor, id, request, ct));

    [HttpGet("staff/unpaid")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Unpaid([FromQuery] string? search, CancellationToken ct) =>
        AsUser(actor => service.Unpaid(actor, search, ct));

    [HttpPost("staff/rentals/{id:int}/outstanding/collect")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> CollectOutstanding(int id, LockerCashRequest request, CancellationToken ct) =>
        AsUser(actor => service.CollectOutstanding(actor, id, request.Amount, ct));

    [HttpGet("staff/rules")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> DeskRules(CancellationToken ct) => AsUser(actor => service.Rules(actor, DeskRole, ct));

    [HttpPost("staff/rentals/{id:int}/clearance")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> RequestClearance(int id, LockerClearanceRequest request, CancellationToken ct) =>
        AsUser(actor => service.RequestClearance(actor, id, request, ct));

    [HttpGet("manager/catalog")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> Catalog(CancellationToken ct) => AsUser(actor => service.Catalog(actor, ct));

    [HttpPost("manager/zones")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> AddZone(LockerZoneRequest request, CancellationToken ct) =>
        AsUser(actor => service.AddZone(actor, request, ct));

    [HttpDelete("manager/zones/{id:int}")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DeleteZone(int id, CancellationToken ct) =>
        AsUser(actor => service.DeleteZone(actor, id, ct));

    [HttpPost("manager/lockers")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> AddLocker(LockerCreateRequest request, CancellationToken ct) =>
        AsUser(actor => service.AddLocker(actor, request, ct));

    [HttpPost("manager/lockers/{id:int}/deactivate")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DeactivateLocker(int id, CancellationToken ct) =>
        AsUser(actor => service.DeactivateLocker(actor, id, ct));

    [HttpPut("manager/rules")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> SaveRules(LockerRulesRequest request, CancellationToken ct) =>
        AsUser(actor => service.SaveRules(actor, request, ct));

    [HttpGet("manager/clearances")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> Clearances(CancellationToken ct) => AsUser(actor => service.Clearances(actor, ct));

    [HttpPost("manager/clearances/{id:int}/verify")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> VerifyClearance(int id, CancellationToken ct) =>
        AsUser(actor => service.VerifyClearance(actor, id, ct));

    [HttpPut("staff/grid/{id:int}/maintenance")]
    [Authorize(Roles = "Staff,Manager")]
    public Task<IActionResult> Maintenance(int id, LockerMaintenanceRequest request, CancellationToken ct) =>
        AsUser(actor => service.SetMaintenance(actor, DeskRole, id, request, ct));
}
