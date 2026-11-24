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
        return this.ToActionResult(await _service.ListAsync(request, userId, cancellationToken));
    }

    [HttpGet("manager")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> List([FromQuery] FeedbackListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.ListAsync(request, memberUserId: null, cancellationToken));

    [HttpPost("manager/{id:int}/approve")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        await ManagerAction(id, (userId, ip) => _service.ApproveAsync(id, userId, ip, cancellationToken));

    [HttpPost("manager/{id:int}/reject")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Reject(int id, [FromBody] FeedbackReplyRequest request, CancellationToken cancellationToken) =>
        await ManagerAction(id, (userId, ip) => _service.RejectAsync(id, request, userId, ip, cancellationToken));

    [HttpPost("manager/{id:int}/reply")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Reply(int id, [FromBody] FeedbackReplyRequest request, CancellationToken cancellationToken) =>
        await ManagerAction(id, (userId, ip) => _service.ReplyAsync(id, request, userId, ip, cancellationToken));

    [HttpPatch("manager/{id:int}/visibility")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Visibility(int id, [FromQuery] bool visible, CancellationToken cancellationToken) =>
        await ManagerAction(id, (userId, ip) => _service.SetVisibilityAsync(id, visible, userId, ip, cancellationToken));

    private async Task<IActionResult> ManagerAction(
        int id,
        Func<string, string?, Task<VFMS.Contracts.Common.Result<VFMS.Contracts.Feedback.FeedbackResponse>>> action)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await action(userId, HttpContext.Connection.RemoteIpAddress?.ToString()));
    }
}
