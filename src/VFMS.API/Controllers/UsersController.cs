using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Users;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserAdministrationService _userAdministrationService;

    public UsersController(IUserAdministrationService userAdministrationService)
    {
        _userAdministrationService = userAdministrationService;
    }

    [HttpPost("walk-in-customer")]
    [Authorize(Roles = "Staff,Manager")]
    [ProducesResponseType(typeof(WalkInCustomerResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(409)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> CreateWalkInCustomer(
        [FromBody] WalkInCustomerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var actorUserId))
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var result = await _userAdministrationService.CreateWalkInCustomerAsync(
            request,
            actorUserId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPost("{userId}/resend-setup-email")]
    [Authorize(Roles = "Staff,Manager")]
    [EnableRateLimiting(PasswordRecoveryModule.CustomerResendPolicy)]
    [ProducesResponseType(typeof(ResendSetupEmailResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(409)]
    [ProducesResponseType(429)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> ResendCustomerSetupEmail(
        string userId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var actorUserId))
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var result = await _userAdministrationService.ResendCustomerSetupEmailAsync(
            userId,
            actorUserId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPost("find-pending-customer")]
    [Authorize(Roles = "Staff,Manager")]
    [EnableRateLimiting(PasswordRecoveryModule.CustomerLookupPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Order = -3000)]
    [ProducesResponseType(typeof(WalkInCustomerResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> FindPendingCustomer(
        [FromBody] FindPendingCustomerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
            return Unauthorized();

        return this.ToActionResult(await _userAdministrationService.FindPendingCustomerAsync(request, cancellationToken));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _userAdministrationService.GetMyProfileAsync(userId, cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _userAdministrationService.UpdateMyProfileAsync(userId, request, cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPut("me/change-password")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ChangeMyPassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _userAdministrationService.ChangeMyPasswordAsync(userId, request, cancellationToken);
        return this.ToActionResult(result);
    }

    private bool TryGetUserId(out string userId)
    {
        userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            string.Empty;
        return !string.IsNullOrWhiteSpace(userId);
    }
}
