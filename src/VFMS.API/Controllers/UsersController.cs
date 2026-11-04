using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateWalkInCustomer(
        [FromBody] WalkInCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _userAdministrationService.CreateWalkInCustomerAsync(request, cancellationToken);
        return this.ToActionResult(result);
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
