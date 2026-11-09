using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-renewals")]
[Authorize(Roles = nameof(UserRole.Member))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipRenewalsController(IMembershipPurchaseService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MembershipRenewalOption>), 200)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        User.GetUserId() is { } memberId
            ? this.ToActionResult(await service.ListRenewalsAsync(memberId, ct))
            : Unauthorized();

    [HttpPost]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 201)]
    [ProducesResponseType(typeof(MembershipPurchaseResponse), 200)]
    public async Task<IActionResult> Renew(RenewMembershipRequest request, CancellationToken ct) =>
        User.GetUserId() is { } memberId
            ? this.ToActionResult(await service.RenewAsync(memberId, request.MembershipId, ct))
            : Unauthorized();
}
