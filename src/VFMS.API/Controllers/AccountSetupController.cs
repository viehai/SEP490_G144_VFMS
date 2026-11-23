using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Auth;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(PasswordRecoveryModule.CustomerSetupPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Order = -3000)]
[Produces("application/json")]
public sealed class AccountSetupController(IUserAdministrationService userAdministrationService) : ControllerBase
{
    /// <summary>Consumes a single-use 48-hour customer password setup token and activates the account.</summary>
    [HttpPost("setup-password")]
    [ProducesResponseType(typeof(SetupPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetupPassword(
        [FromBody] SetupPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userAdministrationService.SetupCustomerPasswordAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return this.ToActionResult(result);
    }
}
