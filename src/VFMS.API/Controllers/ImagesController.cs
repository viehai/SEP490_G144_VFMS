using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;

namespace VFMS.API.Controllers;

/// <summary>
/// Photos of equipment and supplement products. The Manager uploads them; anyone can view or download them,
/// since they appear in the store and on the gym pages. An image never changes once uploaded, so it is cached for a year.
/// </summary>
[ApiController]
[Route("api/images")]
public sealed class ImagesController(IUploadedImageService images) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Manager")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedImages.MaximumBytes + 65_536)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadedImages.MaximumBytes + 65_536)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(await images.UploadAsync(userId, file.FileName, file.Length, content, cancellationToken));
    }

    [HttpGet("{key:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid key, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var result = await images.GetAsync(key, cancellationToken);
        if (!result.IsSuccess)
            return this.ToActionResult(result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var image = result.Data!;
        // ?download=true asks the browser to save the file instead of showing it.
        return download ? File(image.Content, image.ContentType, image.FileName) : File(image.Content, image.ContentType);
    }
}
