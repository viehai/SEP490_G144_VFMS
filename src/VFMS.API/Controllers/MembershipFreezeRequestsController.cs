using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-freeze-requests")]
[Authorize(Roles = nameof(UserRole.Member))]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipFreezeRequestsController(IMembershipFreezeRequestService service) : ControllerBase
{
    [HttpGet("~/api/memberships/{membershipId:int}/freeze-eligibility")]
    [ProducesResponseType(typeof(MembershipFreezeEligibilityResponse), 200)]
    public Task<IActionResult> Eligibility(int membershipId, [FromQuery] int? excludeRequestId, CancellationToken ct) =>
        Run(memberId => service.GetEligibilityAsync(memberId, membershipId, excludeRequestId, ct));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MembershipFreezeRequestResponse>), 200)]
    public Task<IActionResult> List(CancellationToken ct) => Run(memberId => service.ListAsync(memberId, ct));

    [HttpGet("{requestId:int}")]
    [ProducesResponseType(typeof(MembershipFreezeRequestResponse), 200)]
    public Task<IActionResult> Get(int requestId, CancellationToken ct) => Run(memberId => service.GetAsync(memberId, requestId, ct));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MembershipFreezeDocumentLimits.MaximumBytes + 65_536)]
    [RequestFormLimits(MultipartBodyLengthLimit = MembershipFreezeDocumentLimits.MaximumBytes + 65_536)]
    [ProducesResponseType(typeof(MembershipFreezeRequestResponse), 201)]
    public async Task<IActionResult> Create([FromForm] MembershipFreezeRequest request, IFormFile? file, CancellationToken ct)
    {
        if (User.GetUserId() is not { } memberId)
            return Unauthorized();
        await using var content = file?.OpenReadStream();
        var upload = file is null ? null : new MembershipFreezeDocumentUpload(file.FileName, file.ContentType, file.Length, content!);
        return this.ToActionResult(await service.CreateAsync(memberId, request, upload, ct));
    }

    [HttpPut("{requestId:int}")]
    [ProducesResponseType(typeof(MembershipFreezeRequestResponse), 200)]
    public Task<IActionResult> Update(int requestId, MembershipFreezeRequest request, CancellationToken ct) =>
        Run(memberId => service.UpdateAsync(memberId, requestId, request, ct));

    [HttpPost("{requestId:int}/cancel")]
    [ProducesResponseType(typeof(MembershipFreezeRequestResponse), 200)]
    public Task<IActionResult> Cancel(int requestId, CancellationToken ct) => Run(memberId => service.CancelAsync(memberId, requestId, ct));

    [HttpPost("{requestId:int}/early-unfreeze")]
    [ProducesResponseType(typeof(MembershipFreezeRequestResponse), 200)]
    public Task<IActionResult> EarlyUnfreeze(int requestId, CancellationToken ct) =>
        Run(memberId => service.RequestEarlyUnfreezeAsync(memberId, requestId, ct));

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action) =>
        User.GetUserId() is { } memberId ? this.ToActionResult(await action(memberId)) : Unauthorized();
}
