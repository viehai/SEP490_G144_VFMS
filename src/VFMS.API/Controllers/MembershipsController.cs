using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/memberships")]
[Authorize(Roles = nameof(UserRole.Member))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipsController(IMembershipPurchaseService service) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(IReadOnlyList<MembershipSummaryResponse>), 200)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        User.GetUserId() is { } memberId
            ? this.ToActionResult(await service.ListOwnedMembershipsAsync(memberId, ct))
            : Unauthorized();

    [HttpGet("{membershipId:int}")]
    [ProducesResponseType(typeof(MembershipDetailResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int membershipId, CancellationToken ct) =>
        User.GetUserId() is { } memberId
            ? this.ToActionResult(await service.GetOwnedMembershipAsync(memberId, membershipId, ct))
            : Unauthorized();
}
