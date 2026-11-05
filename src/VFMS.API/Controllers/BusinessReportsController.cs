using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;

namespace VFMS.API.Controllers;

/// <summary>UC-86 View Business Reports (Manager, Admin). Reports are read-only snapshots (GBR-63).</summary>
[ApiController]
[Route("api/business-reports")]
[Authorize(Roles = "Manager,Admin")]
[Produces("application/json")]
public sealed class BusinessReportsController : ControllerBase
{
    private readonly IBusinessReportService _service;

    public BusinessReportsController(IBusinessReportService service)
    {
        _service = service;
    }

    /// <summary>Generated reports, newest first. From/To keep reports whose period overlaps that range.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BusinessReportSummaryResponse>), 200)]
    public async Task<IActionResult> GetReports([FromQuery] BusinessReportListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetReportsAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BusinessReportResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetReport(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetReportAsync(id, cancellationToken));

    /// <summary>
    /// Generate and save a report for a past Day, Week or Month. Returns 409 BUSINESS_REPORT_EXISTS when the period
    /// already has a report, unless confirmNew is true.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BusinessReportResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Generate([FromBody] GenerateBusinessReportRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.GenerateAsync(
            request, userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }
}
