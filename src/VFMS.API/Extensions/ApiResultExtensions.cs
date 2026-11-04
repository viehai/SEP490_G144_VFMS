using Microsoft.AspNetCore.Mvc;
using VFMS.Contracts.Common;

namespace VFMS.API.Extensions;

public static class ApiResultExtensions
{
    public static IActionResult ToActionResult<T>(
        this ControllerBase controller,
        Result<T> result)
    {
        if (result.IsSuccess)
            return new ObjectResult(result.Data) { StatusCode = result.StatusCode };

        var problem = new ProblemDetails
        {
            Status = result.StatusCode,
            Title = "The request could not be completed.",
            Detail = result.ErrorMessage,
            Instance = controller.HttpContext.Request.Path
        };
        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            problem.Extensions["code"] = result.ErrorCode;

        return new ObjectResult(problem)
        {
            StatusCode = result.StatusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
