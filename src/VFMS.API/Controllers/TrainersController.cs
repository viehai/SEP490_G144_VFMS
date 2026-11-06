using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VFMS.API.Extensions;
using VFMS.Application.Services;
using VFMS.Contracts.Trainers;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/trainers")]
[RequestSizeLimit(36 * 1024 * 1024)]
public sealed class TrainersController(TrainerService service) : ControllerBase
{
    private string Actor => User.GetUserId()!;
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Search([FromQuery] TrainerSearch query, CancellationToken ct) => this.ToActionResult(await service.Search(query, ct));
    [HttpGet("{id:int}"), AllowAnonymous]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToActionResult(await service.Public(id, ct));
    [HttpPost("applications"), AllowAnonymous, EnableRateLimiting("trainer-public")]
    public async Task<IActionResult> Submit(SubmitTrainerApplicationRequest request, CancellationToken ct) => this.ToActionResult(await service.Submit(request, User.GetUserId(), ct));
    [HttpGet("applications"), Authorize(Roles = "Manager")]
    public async Task<IActionResult> Applications([FromQuery] TrainerApplicationFilter query, CancellationToken ct) => this.ToActionResult(await service.Applications(query, Actor, ct));
    [HttpGet("applications/{id:int}"), Authorize(Roles = "Manager")]
    public async Task<IActionResult> Application(int id, CancellationToken ct) => this.ToActionResult(await service.Application(id, Actor, ct));
    [HttpPost("applications/{id:int}/review"), Authorize(Roles = "Manager")]
    public async Task<IActionResult> Review(int id, ReviewTrainerApplicationRequest request, CancellationToken ct) => this.ToActionResult(await service.Review(id, request, Actor, ct));
    [HttpPost("applications/{id:int}/setup-email"), Authorize(Roles = "Manager"), EnableRateLimiting("trainer-public")]
    public async Task<IActionResult> Resend(int id, CancellationToken ct) => this.ToActionResult(await service.ResendSetup(id, Actor, ct));
    [HttpPut("recruitment-restrictions"), Authorize(Roles = "Manager")]
    public async Task<IActionResult> Restrict(RecruitmentRestrictionRequest request, CancellationToken ct) => this.ToActionResult(await service.Restrict(request, Actor, ct));
    [HttpPost("setup-password"), AllowAnonymous, EnableRateLimiting("trainer-public")]
    public async Task<IActionResult> Setup(TrainerSetupRequest request, CancellationToken ct) => this.ToActionResult(await service.Setup(request, ct));
    [HttpGet("me"), Authorize(Roles = "PT")]
    public async Task<IActionResult> Mine(CancellationToken ct) => this.ToActionResult(await service.Mine(Actor, ct));
    [HttpPut("me"), Authorize(Roles = "PT")]
    public async Task<IActionResult> Update(UpdateTrainerProfileRequest request, CancellationToken ct) => this.ToActionResult(await service.Update(request, Actor, ct));
    [HttpPost("me/credential-changes"), Authorize(Roles = "PT")]
    public async Task<IActionResult> Credentials(TrainerCredentialRequest request, CancellationToken ct) => this.ToActionResult(await service.RequestCredentials(request, Actor, ct));
    [HttpGet("credential-changes"), Authorize(Roles = "PT,Manager")]
    public async Task<IActionResult> Changes(CancellationToken ct, int page = 1, int pageSize = 20) => this.ToActionResult(await service.Changes(Actor, page, pageSize, ct));
    [HttpPost("credential-changes/{id:int}/review"), Authorize(Roles = "Manager")]
    public async Task<IActionResult> ReviewCredentials(int id, ReviewTrainerCredentialRequest request, CancellationToken ct) => this.ToActionResult(await service.ReviewCredentials(id, request, Actor, ct));
    [HttpGet("documents/{id:int}"), AllowAnonymous]
    public async Task<IActionResult> Document(int id, CancellationToken ct)
    {
        var result = await service.Document(id, User.GetUserId(), ct); if (!result.IsSuccess) return this.ToActionResult(result);
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers.CacheControl = "private, no-store";
        return File(result.Data!.Content, result.Data.ContentType, result.Data.FileName);
    }
}
