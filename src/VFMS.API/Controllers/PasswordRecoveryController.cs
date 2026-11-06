using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Auth;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("password-recovery")]
[Produces("application/json")]
public sealed class PasswordRecoveryController(IPasswordResetService service) : ControllerBase
{
    /// <summary>Queues recovery instructions without disclosing whether the email is registered.</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(PasswordResetResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await service.RequestAsync(request, cancellationToken);
        return result.IsSuccess ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { result.ErrorCode, result.ErrorMessage });
    }

    /// <summary>Consumes a single-use reset token and revokes all existing refresh tokens.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(PasswordResetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await service.ResetAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { result.ErrorCode, result.ErrorMessage });
    }
}
