using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-purchases")]
[Authorize(Roles = Buyers)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipPurchasesController(IMembershipPurchaseService service) : ControllerBase
{
    private const string Buyers = nameof(UserRole.Customer) + "," + nameof(UserRole.Member);

    [HttpPost]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 201)]
    public async Task<IActionResult> Create(MembershipPurchaseRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await service.CreateAsync(userId, request, ct)) : Unauthorized();

    [HttpGet("{orderNumber}")]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> Get(string orderNumber, CancellationToken ct) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await service.GetAsync(userId, orderNumber, ct)) : Unauthorized();

    [HttpPost("{orderNumber}/voucher")]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> ApplyVoucher(string orderNumber, ApplyMembershipVoucherRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.ApplyVoucherAsync(userId, orderNumber, request, ct)) : Unauthorized();

    [HttpDelete("{orderNumber}/voucher")]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> RemoveVoucher(string orderNumber, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.RemoveVoucherAsync(userId, orderNumber, ct)) : Unauthorized();

    [HttpPost("{orderNumber}/complete-no-charge")]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> CompleteNoCharge(string orderNumber, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.CompleteNoChargeAsync(userId, orderNumber, ct)) : Unauthorized();
}

[ApiController]
[Route("api/staff/membership-purchases")]
[Authorize(Roles = nameof(UserRole.Staff))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffMembershipPurchasesController(IMembershipPurchaseService service) : ControllerBase
{
    [HttpPost("{orderNumber}/confirm-cash")]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> ConfirmCash(string orderNumber, ConfirmMembershipCashRequest request, CancellationToken ct) =>
        User.GetUserId() is { } staffId
            ? this.ToActionResult(await service.ConfirmCashAsync(staffId, orderNumber, request.AmountReceived,
                HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
            : Unauthorized();
}
