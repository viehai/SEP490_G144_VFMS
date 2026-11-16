using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipTransactionRepository(VfmsDbContext context) : IMembershipTransactionRepository
{
    public async Task<(int TotalCount, IReadOnlyList<OwnedMembershipTransaction> Items)> ListAsync(
        string purchaserId, int page, int pageSize, CancellationToken ct)
    {
        var query = OwnedOrders(purchaserId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(SummaryProjection).ToListAsync(ct);
        return (total, items);
    }

    public Task<OwnedMembershipTransactionDetail?> FindAsync(string purchaserId, int orderId, CancellationToken ct) =>
        OwnedOrders(purchaserId).Where(order => order.Id == orderId)
            .Select(order => new OwnedMembershipTransactionDetail(
                new OwnedMembershipTransaction(order.Id, order.OrderNumber, order.CreatedAt, order.CompletedAt,
                    order.OrderStatus, order.PaymentTransaction!.PaymentStatus, order.PaymentTransaction.PaymentMethod,
                    order.Items.Any(item => item.RenewedFromMembershipId != null),
                    order.Items.OrderBy(item => item.Id).Select(item => item.PackageNameSnapshot).First()!,
                    order.Items.Count, order.PaymentTransaction.AmountDue, order.PaymentTransaction.AmountReceived,
                    order.PaymentTransaction.Invoice == null ? null : order.PaymentTransaction.Invoice.InvoiceNumber),
                order.PaymentTransaction.PaidAt,
                order.Items.OrderBy(item => item.Id).Select(item => new OwnedMembershipTransactionItem(
                    item.PackageNameSnapshot, item.UnitPriceSnapshot, item.BeneficiaryUserId == purchaserId,
                    item.ActivationStartDate, item.RenewedFromMembershipId != null)).ToList(),
                order.VoucherRedemption == null ? null : new OwnedMembershipTransactionVoucher(
                    order.VoucherRedemption.VoucherCodeSnapshot, order.VoucherRedemption.VoucherNameSnapshot,
                    order.VoucherRedemption.DiscountAmount, order.VoucherRedemption.RedemptionStatus),
                order.PaymentTransaction.Invoice == null ? null : new OwnedMembershipTransactionInvoice(
                    order.PaymentTransaction.Invoice.InvoiceNumber, order.PaymentTransaction.Invoice.IssuedAt,
                    order.PaymentTransaction.Invoice.InvoiceStatus), order.Purchaser.FullName))
            .SingleOrDefaultAsync(ct);

    // Order is the canonical purchase aggregate; supplement orders use separate tables.
    // No active membership, current package, beneficiary account, or mutable voucher is joined.
    // Detail exposes only the purchaser's current display name, which has no historical snapshot.
    private IQueryable<Order> OwnedOrders(string purchaserId) => context.Orders.AsNoTracking()
        .Where(order => order.PurchaserUserId == purchaserId && order.PaymentTransaction != null && order.Items.Any());

    private static readonly Expression<Func<Order, OwnedMembershipTransaction>> SummaryProjection = order =>
        new(order.Id, order.OrderNumber, order.CreatedAt, order.CompletedAt, order.OrderStatus,
            order.PaymentTransaction!.PaymentStatus, order.PaymentTransaction.PaymentMethod,
            order.Items.Any(item => item.RenewedFromMembershipId != null),
            order.Items.OrderBy(item => item.Id).Select(item => item.PackageNameSnapshot).First()!, order.Items.Count,
            order.PaymentTransaction.AmountDue, order.PaymentTransaction.AmountReceived,
            order.PaymentTransaction.Invoice == null ? null : order.PaymentTransaction.Invoice.InvoiceNumber);
}
