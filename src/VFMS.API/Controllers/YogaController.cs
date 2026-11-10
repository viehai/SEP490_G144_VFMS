using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;

namespace VFMS.API.Controllers;

/// <summary>UC-34 Manage Yoga Studios (Manager).</summary>
[ApiController]
[Route("api/yoga/studios")]
[Authorize(Roles = "Manager")]
[Produces("application/json")]
public sealed class YogaStudiosController : ControllerBase
{
    private readonly IYogaStudioService _service;

    public YogaStudiosController(IYogaStudioService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetStudiosAsync(includeInactive, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] YogaStudioRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CreateAsync(request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] YogaStudioRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.SetActiveAsync(id, false, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.SetActiveAsync(id, true, userId, ClientIp, cancellationToken))
            : Unauthorized();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// UC-35 Manage Yoga Classes and UC-36 Manage Yoga Class Schedules (Manager),
/// UC-37 View Yoga Schedule (public), UC-41 View Assigned Yoga Classes (instructor).
/// </summary>
[ApiController]
[Route("api/yoga")]
[Authorize(Roles = "Manager")]
[Produces("application/json")]
public sealed class YogaClassesController : ControllerBase
{
    private readonly IYogaClassService _service;

    public YogaClassesController(IYogaClassService service) => _service = service;

    [HttpGet("class-types")]
    [AllowAnonymous]
    public async Task<IActionResult> GetClassTypes(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetClassTypesAsync(cancellationToken));

    [HttpPost("class-types")]
    public async Task<IActionResult> CreateClassType([FromBody] YogaClassTypeRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CreateClassTypeAsync(request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("class-types/{id:int}")]
    public async Task<IActionResult> UpdateClassType(int id, [FromBody] YogaClassTypeRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateClassTypeAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpGet("instructors")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInstructors(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetInstructorsAsync(cancellationToken));

    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetClassesAsync(cancellationToken));

    [HttpGet("classes/{id:int}")]
    public async Task<IActionResult> GetClass(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetClassAsync(id, cancellationToken));

    [HttpPost("classes")]
    public async Task<IActionResult> CreateClass([FromBody] YogaClassRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CreateClassAsync(request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("classes/{id:int}")]
    public async Task<IActionResult> UpdateClass(int id, [FromBody] YogaClassRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateClassAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("classes/{id:int}/publish")]
    public async Task<IActionResult> PublishClass(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.PublishClassAsync(id, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("classes/{id:int}/cancel")]
    public async Task<IActionResult> CancelClass(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CancelClassAsync(id, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpGet("classes/{id:int}/sessions")]
    public async Task<IActionResult> GetClassSessions(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetClassSessionsAsync(id, cancellationToken));

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] YogaSessionRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CreateSessionAsync(request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("sessions/{id:int}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] YogaSessionRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateSessionAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("sessions/{id:int}/cancel")]
    public async Task<IActionResult> CancelSession(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CancelSessionAsync(id, userId, ClientIp, cancellationToken))
            : Unauthorized();

    /// <summary>UC-37 View Yoga Schedule: public timetable with free seats.</summary>
    [HttpGet("schedule")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSchedule([FromQuery] YogaScheduleQuery query, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetPublishedScheduleAsync(query, cancellationToken));

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>UC-41 View Assigned Yoga Classes: the signed-in instructor's sessions and rosters.</summary>
[ApiController]
[Route("api/yoga/assigned")]
[Authorize(Roles = YogaClassService.InstructorRole)]
[Produces("application/json")]
public sealed class YogaInstructorController : ControllerBase
{
    private readonly IYogaClassService _service;

    public YogaInstructorController(IYogaClassService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAssigned([FromQuery] YogaScheduleQuery query, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.GetAssignedSessionsAsync(userId, query with { InstructorUserId = null }, cancellationToken))
            : Unauthorized();
}

/// <summary>UC-38 Book Yoga Class &amp; Reserve Mat (Member).</summary>
[ApiController]
[Route("api/yoga/bookings")]
[Authorize(Roles = "Member")]
[Produces("application/json")]
public sealed class YogaBookingsController : ControllerBase
{
    private readonly IYogaBookingService _service;

    public YogaBookingsController(IYogaBookingService service) => _service = service;

    /// <summary>Studio floorplan: which mats are free.</summary>
    [HttpGet("classes/{classId:int}/mats")]
    public async Task<IActionResult> GetMats(int classId, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetMatMapAsync(classId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Book([FromBody] YogaBookingRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.BookAsync(userId, request, cancellationToken))
            : Unauthorized();
}
