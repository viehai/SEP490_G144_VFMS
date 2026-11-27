using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VFMS.API.Extensions;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/checkout-refunds")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
// Read-only list of transfers received but not applied to an order (GBR-102). The front desk settles them outside the system.
public sealed class CheckoutRefundsController(VfmsDbContext db) : ControllerBase
{
    [HttpGet("mine")]
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (User.GetUserId() is not { } actor)
            return Unauthorized();
        var records = await RefundRequests.AsNoTracking()
            .Where(r => r.Order.PurchaserUserId == actor).OrderByDescending(r => r.Id).Take(100).ToListAsync(ct);
        return Ok(records.Select(View));
    }

    [HttpGet("pending")]
    [Authorize(Roles = "Staff,Manager")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var staff = await Staff(ct);
        if (staff is null)
            return Forbid();
        var records = await RefundRequests.AsNoTracking()
            .Where(r => r.Order.Purchaser.GymId == staff.GymId && r.RefundStatus != "Refunded")
            .OrderBy(r => r.Id).Take(100).ToListAsync(ct);
        return Ok(records.Select(View));
    }

    private Task<ApplicationUser?> Staff(CancellationToken ct)
    {
        var actor = User.GetUserId();
        return db.ApplicationUsers.SingleOrDefaultAsync(u => u.UserId == actor && u.AccountStatus == AccountStatus.Active &&
            !u.IsBanned && u.UserRoles.Any(r => r.Role.RoleName == "Staff" || r.Role.RoleName == "Manager"), ct);
    }

    private IQueryable<PaymentTransaction> RefundRequests => db.PaymentTransactions
        .Where(p => p.RefundStatus != null && p.RefundAmount > 0 && p.PaidAt != null &&
            p.RefundReason != LockerService.DepositReturnReason && p.RefundReason != "DailyDepositReturn" &&
            p.GatewayTransactionReference != null)
        .Include(p => p.Order).Include(p => p.SourcePaymentTransaction).ThenInclude(p => p!.Order);

    private static CheckoutRefundView View(PaymentTransaction refund) =>
        new(refund.Id, (refund.SourcePaymentTransaction?.Order ?? refund.Order).OrderNumber,
            refund.RefundAmount!.Value, refund.RefundStatus!, DateTime.SpecifyKind(refund.PaidAt!.Value, DateTimeKind.Utc),
            refund.RefundResolvedAt is { } resolved ? DateTime.SpecifyKind(resolved, DateTimeKind.Utc) : null,
            refund.GatewayTransactionReference!, refund.RefundReference);
}
