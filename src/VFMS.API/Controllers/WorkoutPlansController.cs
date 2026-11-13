using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Workout;

namespace VFMS.API.Controllers;

/// <summary>UC-49/UC-58 Generate and UC-50/UC-59 Manage Personalized Workout Plan (Customer, Member).</summary>
[ApiController]
[Route("api/workout")]
[Authorize(Roles = "Customer,Member")]
[Produces("application/json")]
public sealed class WorkoutPlansController : ControllerBase
{
    private readonly IWorkoutPlanService _service;

    public WorkoutPlansController(IWorkoutPlanService service) => _service = service;

    [HttpGet("survey")]
    public async Task<IActionResult> GetSurvey(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GetSurveyAsync(userId, cancellationToken)) : Unauthorized();

    [HttpPut("survey")]
    public async Task<IActionResult> SaveSurvey([FromBody] FitnessSurveyRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.SaveSurveyAsync(userId, request, cancellationToken)) : Unauthorized();

    [HttpPost("plans/generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateWorkoutPlanRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GenerateAsync(userId, request, cancellationToken)) : Unauthorized();

    [HttpGet("plans/active")]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GetActivePlanAsync(userId, cancellationToken)) : Unauthorized();

    [HttpGet("plans")]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GetHistoryAsync(userId, cancellationToken)) : Unauthorized();

    [HttpGet("plans/{planId:int}")]
    public async Task<IActionResult> GetPlan(int planId, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GetPlanAsync(userId, planId, cancellationToken)) : Unauthorized();

    [HttpPatch("plans/{planId:int}/items/{itemId:int}/completion")]
    public async Task<IActionResult> SetCompletion(int planId, int itemId, [FromBody] SetPlanItemCompletionRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.SetItemCompletionAsync(userId, planId, itemId, request, cancellationToken))
            : Unauthorized();

    [HttpPut("plans/{planId:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int planId, int itemId, [FromBody] UpdatePlanItemRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.UpdateItemAsync(userId, planId, itemId, request, cancellationToken))
            : Unauthorized();

    [HttpPost("plans/{planId:int}/archive")]
    public async Task<IActionResult> Archive(int planId, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.ArchivePlanAsync(userId, planId, cancellationToken))
            : Unauthorized();

    [HttpGet("equipment")]
    public async Task<IActionResult> GetEquipment(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetEquipmentAsync(cancellationToken));

    [HttpGet("exercises")]
    public async Task<IActionResult> GetExercises(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetExercisesAsync(cancellationToken));
}
