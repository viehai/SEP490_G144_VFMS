using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;

namespace VFMS.API.Controllers;

/// <summary>UC-87 View System Audit Logs (Admin, read-only).</summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _service;

    public AuditLogsController(IAuditLogService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AuditLogResponse>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetLogs([FromQuery] AuditLogListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetLogsAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AuditLogResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetLog(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetLogAsync(id, cancellationToken));
}
