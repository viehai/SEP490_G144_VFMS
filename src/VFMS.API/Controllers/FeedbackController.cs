using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Feedback;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/feedback")]
[Authorize]
[Produces("application/json")]
public sealed class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _service;

    public FeedbackController(IFeedbackService service) => _service = service;

    [AllowAnonymous]
    [HttpGet("~/api/packages/{packageId:int}/reviews")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PackageReviews(
        int packageId, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int? rating = null)
    {
        var memberId = User.IsInRole("Member") || User.IsInRole("Customer") ? User.GetUserId() : null;
        return this.ToActionResult(await _service.GetPackageReviewsAsync(packageId, memberId, page, rating, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("public")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Public([FromQuery] FeedbackListRequest request, CancellationToken cancellationToken)
    {
        var memberId = User.IsInRole("Member") || User.IsInRole("Customer") ? User.GetUserId() : null;
        return this.ToActionResult(await _service.ListPublicAsync(request, memberId, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Submit([FromBody] FeedbackSubmitRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.SubmitAsync(request, userId, cancellationToken));
    }

    [HttpGet("trainers")]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Trainers(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetTrainersAsync(cancellationToken));

    [HttpGet("mine")]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Mine([FromQuery] FeedbackListRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.ListMineAsync(request, userId, cancellationToken));
    }

    [HttpGet("manager")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> ManagementList(
        [FromQuery] FeedbackListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.ListForManagementAsync(request, cancellationToken));

    [HttpPost("manager/{id:int}/reply")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Reply(
        int id, [FromBody] FeedbackReplyRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.ReplyAsync(
            id, request, userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Update(int id, [FromBody] FeedbackUpdateRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.UpdateAsync(id, request, userId, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        var result = await _service.DeleteAsync(id, userId, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result);
    }
}
