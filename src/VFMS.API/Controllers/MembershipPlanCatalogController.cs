using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-plans")]
[Authorize(Roles = nameof(UserRole.Customer) + "," + nameof(UserRole.Member))]
[Produces("application/json")]
public sealed class MembershipPlanCatalogController(IMembershipPlanService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MembershipPlanCatalogItemResponse>), 200)]
    public async Task<IActionResult> List([FromQuery] MembershipPlanListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await service.ListCatalogAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MembershipPlanCatalogItemResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await service.GetCatalogItemAsync(id, cancellationToken));
}
