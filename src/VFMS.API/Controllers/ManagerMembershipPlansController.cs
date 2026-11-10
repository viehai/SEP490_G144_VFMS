using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/manager/membership-plans")]
[Authorize(Roles = nameof(UserRole.Manager))]
[Produces("application/json")]
public sealed class ManagerMembershipPlansController(IMembershipPlanService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MembershipPlanResponse>), 200)]
    public async Task<IActionResult> List([FromQuery] MembershipPlanListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await service.ListAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MembershipPlanResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(MembershipPlanResponse), 201)]
    [ProducesResponseType(400)]
    public Task<IActionResult> Create([FromBody] MembershipPlanRequest request, CancellationToken cancellationToken) =>
        Run(userId => service.CreateAsync(request, userId, ClientIp, cancellationToken));

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(MembershipPlanResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Update(int id, [FromBody] MembershipPlanRequest request, CancellationToken cancellationToken) =>
        Run(userId => service.UpdateAsync(id, request, userId, ClientIp, cancellationToken));

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(MembershipPlanResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> ChangeStatus(
        int id, [FromBody] MembershipPlanStatusRequest request, CancellationToken cancellationToken) =>
        Run(userId => service.ChangeStatusAsync(id, request, userId, ClientIp, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(MembershipPlanDeleteResponse), 200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        Run(userId => service.DeleteAsync(id, userId, ClientIp, cancellationToken));

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId));
    }
}
