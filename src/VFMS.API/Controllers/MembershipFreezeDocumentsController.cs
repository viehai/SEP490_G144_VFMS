using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-freeze-requests/{requestId:int}/document")]
[Authorize(Roles = "Member,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipFreezeDocumentsController(IMembershipFreezeDocumentService documents) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Member))]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MembershipFreezeDocumentLimits.MaximumBytes + 65_536)]
    [RequestFormLimits(MultipartBodyLengthLimit = MembershipFreezeDocumentLimits.MaximumBytes + 65_536)]
    public async Task<IActionResult> Upload(int requestId, IFormFile file, CancellationToken ct)
    {
        if (User.GetUserId() is not { } memberId)
            return Unauthorized();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(await documents.UploadAsync(
            memberId, requestId, file.FileName, file.ContentType, file.Length, content, ct));
    }

    [HttpDelete]
    [Authorize(Roles = nameof(UserRole.Member))]
    public async Task<IActionResult> Remove(int requestId, CancellationToken ct) =>
        User.GetUserId() is { } memberId
            ? this.ToActionResult(await documents.RemoveAsync(memberId, requestId, ct)) : Unauthorized();

    [HttpGet]
    public async Task<IActionResult> Download(int requestId, CancellationToken ct)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        var result = await documents.DownloadAsync(userId, User.IsInRole(nameof(UserRole.Manager)), requestId, ct);
        if (!result.IsSuccess)
            return this.ToActionResult(result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data!.Content, result.Data.Document.ContentType, result.Data.Document.FileName);
    }
}
