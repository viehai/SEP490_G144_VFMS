using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/membership-transactions")]
[Authorize(Roles = nameof(UserRole.Customer) + "," + nameof(UserRole.Member))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MembershipTransactionsController(
    IMembershipTransactionService service, IMembershipInvoiceExportService invoices) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MembershipTransactionSummaryResponse>), 200)]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 10) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.ListAsync(userId, page, pageSize, ct)) : Unauthorized();

    [HttpGet("{orderId:int}")]
    [ProducesResponseType(typeof(MembershipTransactionDetailResponse), 200)]
    public async Task<IActionResult> Get(int orderId, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.GetAsync(userId, orderId, ct)) : Unauthorized();

    [HttpGet("{orderId:int}/invoice/pdf")]
    [ProducesResponseType(typeof(FileContentResult), 200, "application/pdf")]
    public async Task<IActionResult> ExportInvoice(int orderId, CancellationToken ct)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        var result = await invoices.ExportAsync(userId, orderId, ct);
        if (!result.IsSuccess)
            return this.ToActionResult(result);
        return File(result.Data!.Content, "application/pdf", result.Data.FileName);
    }
}
