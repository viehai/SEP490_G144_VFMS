using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Users;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserAdministrationService _userAdministrationService;

    public AdminUsersController(IUserAdministrationService userAdministrationService)
    {
        _userAdministrationService = userAdministrationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedUsersResponse), 200)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] UserListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _userAdministrationService.GetUsersAsync(request, cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPost("staff")]
    [ProducesResponseType(typeof(UserAccountResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateStaff(
        [FromBody] CreateStaffRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
            return Unauthorized();

        var result = await _userAdministrationService.CreateStaffAsync(
            request, actorUserId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPut("{userId}/roles")]
    [ProducesResponseType(typeof(UserAccountResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateRoles(
        string userId,
        [FromBody] UpdateUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
            return Unauthorized();

        var result = await _userAdministrationService.UpdateRolesAsync(
            userId, request, actorUserId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPatch("{userId}/lock")]
    [ProducesResponseType(typeof(UserAccountResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> LockUser(string userId, CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
            return Unauthorized();

        var result = await _userAdministrationService.LockUserAsync(
            userId, actorUserId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPatch("{userId}/unlock")]
    [ProducesResponseType(typeof(UserAccountResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> UnlockUser(string userId, CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
            return Unauthorized();

        var result = await _userAdministrationService.UnlockUserAsync(
            userId, actorUserId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return this.ToActionResult(result);
    }

    private bool TryGetActorUserId(out string userId)
    {
        userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            string.Empty;
        return !string.IsNullOrWhiteSpace(userId);
    }
}
