using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Workout;

namespace VFMS.API.Controllers;

/// <summary>Manager: the exercise catalog used by AI workout plans and the members' "Đổi bài tập".</summary>
[ApiController]
[Route("api/manager/exercises")]
[Authorize(Roles = "Manager")]
[Produces("application/json")]
public sealed class ExerciseCatalogController : ControllerBase
{
    private readonly IExerciseCatalogService _service;

    public ExerciseCatalogController(IExerciseCatalogService service) => _service = service;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExerciseRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.CreateAsync(request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExerciseRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPut("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] SetExerciseActiveRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.SetActiveAsync(id, request.IsActive, userId, ClientIp, cancellationToken))
            : Unauthorized();
}
