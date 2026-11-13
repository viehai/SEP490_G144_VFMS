using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/manager/membership-freeze-policy")]
[Authorize(Roles = nameof(UserRole.Manager))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerMembershipFreezePolicyController(IMembershipFreezePolicyService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(MembershipFreezePolicyResponse), 200)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        User.GetUserId() is not null ? this.ToActionResult(await service.GetAsync(ct)) : Unauthorized();

    [HttpPut]
    [ProducesResponseType(typeof(MembershipFreezePolicyResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Update([FromBody] MembershipFreezePolicyRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.UpdateAsync(
                request, userId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
            : Unauthorized();
}
