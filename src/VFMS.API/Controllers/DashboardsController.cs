using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/dashboards")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardsController(DashboardQueries queries, TimeProvider clock) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> Admin([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (days is not (7 or 30 or 90)) return BadRequest();
        return Ok(await queries.AdminAsync(clock.GetUtcNow().UtcDateTime, days, ct));
    }

    [Authorize(Roles = "Manager")]
    [HttpGet("manager")]
    public async Task<IActionResult> Manager(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (User.GetUserId() is not { } id || await queries.GymAsync(id, ct) is not { } gym) return Forbid();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        var end = to ?? today;
        if (end < DateOnly.MinValue.AddDays(100) || end > today) return BadRequest();
        var start = from ?? end.AddDays(-6);
        if (start > end || end.DayNumber - start.DayNumber >= 93) return BadRequest();
        return Ok(await queries.ManagerAsync(gym, now, start, end, ct));
    }

    [Authorize(Roles = "Staff")]
    [HttpGet("staff")]
    public async Task<IActionResult> Staff(CancellationToken ct)
    {
        if (User.GetUserId() is not { } id || await queries.GymAsync(id, ct) is not { } gym) return Forbid();
        return Ok(await queries.StaffAsync(gym, clock.GetUtcNow().UtcDateTime, ct));
    }

    [Authorize(Roles = "PT")]
    [HttpGet("pt")]
    public async Task<IActionResult> Trainer(CancellationToken ct) => User.GetUserId() is { } id
        ? Ok(await queries.TrainerAsync(id, User.IsInRole("YogaInstructor"), clock.GetUtcNow().UtcDateTime, ct)) : Unauthorized();

    [Authorize(Roles = "Admin,Manager,Staff,PT")]
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct) => User.GetUserId() is { } id
        ? Ok(await queries.InboxAsync(id, ct)) : Unauthorized();
}
