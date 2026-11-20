using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Packages;

namespace VFMS.API.Controllers;

/// <summary>
/// Public catalog endpoint — không yêu cầu xác thực.
/// Frontend dùng để render /membership-plans?type=gym|yoga|pt
/// </summary>
[ApiController]
[Route("api/packages")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class PackageCatalogController(IMembershipPlanService service) : ControllerBase
{
    /// <summary>
    /// Trả về danh sách package Active theo ComponentType.
    /// </summary>
    /// <param name="request">type (gym|yoga|pt), page, pageSize</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<PackageCatalogItemResponse>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> List(
        [FromQuery] PackageCatalogQuery request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await service.ListPackageCatalogAsync(request, cancellationToken));
}
