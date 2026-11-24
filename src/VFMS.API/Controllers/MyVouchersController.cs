using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController, Route("api/my-vouchers")]
[Authorize(Roles = nameof(UserRole.Customer) + "," + nameof(UserRole.Member))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MyVouchersController(IMyVoucherService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? orderNumber, CancellationToken ct) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await service.ListAsync(userId, orderNumber, ct)) : Unauthorized();
}
