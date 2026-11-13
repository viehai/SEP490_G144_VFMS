using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/manager/membership-freeze-requests")]
[Authorize(Roles = nameof(UserRole.Manager))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerMembershipFreezeRequestsController(IMembershipFreezeReviewService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MembershipFreezeReviewSummaryResponse>), 200)]
    public Task<IActionResult> List([FromQuery] MembershipFreezeReviewListRequest request, CancellationToken ct) =>
        Run(_ => service.ListAsync(request, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MembershipFreezeReviewDetailResponse), 200)]
    public Task<IActionResult> Get(int id, CancellationToken ct, [FromQuery] string type = MembershipFreezeReviewTypes.Freeze) =>
        Run(_ => service.GetAsync(id, type, ct));

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(MembershipFreezeReviewDetailResponse), 200)]
    public Task<IActionResult> Approve(int id, CancellationToken ct) =>
        Run(manager => service.ApproveAsync(manager, id, false, ClientIp, ct));

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(MembershipFreezeReviewDetailResponse), 200)]
    public Task<IActionResult> Reject(int id, RejectMembershipFreezeRequest request, CancellationToken ct) =>
        Run(manager => service.RejectAsync(manager, id, false, request, ClientIp, ct));

    [HttpPost("{id:int}/early-unfreeze/approve")]
    [ProducesResponseType(typeof(MembershipFreezeReviewDetailResponse), 200)]
    public Task<IActionResult> ApproveEarly(int id, CancellationToken ct) =>
        Run(manager => service.ApproveAsync(manager, id, true, ClientIp, ct));

    [HttpPost("{id:int}/early-unfreeze/reject")]
    [ProducesResponseType(typeof(MembershipFreezeReviewDetailResponse), 200)]
    public Task<IActionResult> RejectEarly(int id, RejectMembershipFreezeRequest request, CancellationToken ct) =>
        Run(manager => service.RejectAsync(manager, id, true, request, ClientIp, ct));

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action) =>
        User.GetUserId() is { } manager ? this.ToActionResult(await action(manager)) : Unauthorized();
}
