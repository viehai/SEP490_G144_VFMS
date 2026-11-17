using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Payments;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/checkout-refunds")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutRefundsController(VfmsDbContext db, IMembershipPurchaseRepository commerce, TimeProvider clock)
    : ControllerBase
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

    [HttpPost("{id:int}/outcome")]
    [Authorize(Roles = "Staff,Manager")]
    public async Task<IActionResult> RecordOutcome(int id, CheckoutRefundOutcome input, CancellationToken ct)
    {
        var staff = await Staff(ct);
        if (staff is null)
            return Forbid();
        var reference = input.Reference?.Trim();
        if (input.Succeeded is null || input.Succeeded == true && string.IsNullOrEmpty(reference))
            return BadRequest("Cần kết quả và mã giao dịch hoàn tiền nếu đã hoàn.");
        var result = await commerce.ExecuteInSerializableTransactionAsync(async () =>
        {
            var refund = await RefundRequests
                .SingleOrDefaultAsync(r => r.Id == id && r.Order.Purchaser.GymId == staff.GymId, ct);
            if (refund is null)
                return Result<CheckoutRefundView>.Failure("Không tìm thấy yêu cầu hoàn tiền.", "REFUND_NOT_FOUND", 404);
            var status = input.Succeeded.Value ? "Refunded" : "Failed";
            if (refund.RefundStatus == status)
                return Result<CheckoutRefundView>.Success(View(refund));
            if (refund.RefundStatus == "Refunded")
                return Result<CheckoutRefundView>.Failure("Khoản này đã được hoàn.", "REFUND_ALREADY_PAID", 409);

            var now = clock.GetUtcNow().UtcDateTime;
            CheckoutRefunds.Resolve(db, refund, staff.UserId, input.Succeeded.Value, PaymentMethod.BankTransfer, reference, now);
            await db.SaveChangesAsync(ct);
            return Result<CheckoutRefundView>.Success(View(refund));
        }, ct);
        return this.ToActionResult(result);
    }

    private Task<ApplicationUser?> Staff(CancellationToken ct)
    {
        var actor = User.GetUserId();
        return db.ApplicationUsers.SingleOrDefaultAsync(u => u.UserId == actor && u.AccountStatus == AccountStatus.Active &&
            !u.IsBanned && u.UserRoles.Any(r => r.Role.RoleName == "Staff" || r.Role.RoleName == "Manager"), ct);
    }

    private IQueryable<PaymentTransaction> RefundRequests => db.PaymentTransactions
        .Where(p => p.RefundStatus != null && p.RefundAmount > 0 && p.PaidAt != null &&
            p.RefundReason != "DailyDepositReturn" && p.GatewayTransactionReference != null)
        .Include(p => p.Order).Include(p => p.SourcePaymentTransaction).ThenInclude(p => p!.Order);

    private static CheckoutRefundView View(PaymentTransaction refund) =>
        new(refund.Id, (refund.SourcePaymentTransaction?.Order ?? refund.Order).OrderNumber,
            refund.RefundAmount!.Value, refund.RefundStatus!, DateTime.SpecifyKind(refund.PaidAt!.Value, DateTimeKind.Utc),
            refund.RefundResolvedAt is { } resolved ? DateTime.SpecifyKind(resolved, DateTimeKind.Utc) : null,
            refund.GatewayTransactionReference!, refund.RefundReference);
}
