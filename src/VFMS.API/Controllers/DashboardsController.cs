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

    [Authorize(Roles = "Admin,Manager,Staff,PT,YogaInstructor,Customer,Member")]
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct)
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        if (!await queries.IsActiveUserAsync(id, ct)) return Forbid();
        return Ok(await queries.InboxAsync(id, clock.GetUtcNow().UtcDateTime.AddDays(-90), ct));
    }

    [Authorize(Roles = "Admin,Manager,Staff,PT,YogaInstructor,Customer,Member")]
    [HttpGet("notifications/history")]
    public async Task<IActionResult> NotificationHistory([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        if (!await queries.IsActiveUserAsync(id, ct)) return Forbid();
        if (page < 1 || page > int.MaxValue / 10) return BadRequest();
        return Ok(await queries.NotificationHistoryAsync(id, clock.GetUtcNow().UtcDateTime.AddDays(-90), page, ct));
    }

    [Authorize(Roles = "Admin,Manager,Staff,PT,YogaInstructor,Customer,Member")]
    [HttpPost("notifications/{notificationId:int}/read")]
    public async Task<IActionResult> ReadNotification(int notificationId, CancellationToken ct)
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        if (!await queries.IsActiveUserAsync(id, ct)) return Forbid();
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await queries.MarkNotificationReadAsync(id, notificationId, now, ct)) return NotFound();
        return Ok(await queries.InboxAsync(id, now.AddDays(-90), ct));
    }

    [Authorize(Roles = "Admin,Manager,Staff,PT,YogaInstructor,Customer,Member")]
    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAllNotifications(CancellationToken ct)
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        if (!await queries.IsActiveUserAsync(id, ct)) return Forbid();
        var now = clock.GetUtcNow().UtcDateTime;
        await queries.MarkAllNotificationsReadAsync(id, now, ct);
        return Ok(await queries.InboxAsync(id, now.AddDays(-90), ct));
    }
}
