using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class MembershipPurchaseRepository
{
    public async Task<PagedResponse<StaffPackageOrderResponse>> ListStaffOrdersAsync(int gymId,
        StaffPackageOrderFilter filter, DateTime nowUtc, CancellationToken ct)
    {
        var counterCutoff = nowUtc.AddHours(-PackageOrderTiming.CounterPaymentHours);
        var onlineCutoff = nowUtc.AddMinutes(-PackageOrderTiming.OnlinePaymentMinutes);
        var ptOnlineCutoff = nowUtc.AddMinutes(-PackageOrderTiming.PtOnlineHoldMinutes);

        // A counter PT order gets its payment row only when the cashier confirms receipt.
        var query = from order in context.Orders.AsNoTracking()
            join purchase in context.PTPurchases on (int?)order.Id equals purchase.OrderId into ptPurchases
            from pt in ptPurchases.DefaultIfEmpty()
            let payment = order.PaymentTransaction
            let counter = order.Channel == OrderChannel.Counter
            let isPt = pt != null
            let expired = order.OrderStatus == OrderStatus.Pending && (isPt
                ? pt!.Status == PTPurchaseStatus.Expired || (pt.HoldExpiresAt.HasValue
                    ? pt.HoldExpiresAt <= nowUtc : order.CreatedAt <= (counter ? counterCutoff : ptOnlineCutoff))
                : order.CreatedAt <= (counter ? counterCutoff : onlineCutoff))
            let status = expired ? OrderStatus.Expired : order.OrderStatus
            let method = payment != null ? (PaymentMethod?)payment.PaymentMethod : counter ? PaymentMethod.Cash : (PaymentMethod?)null
            let paymentStatus = payment != null
                ? status == OrderStatus.Expired && payment.PaymentStatus == PaymentStatus.Pending
                    ? (PaymentStatus?)PaymentStatus.Cancelled : payment.PaymentStatus
                : isPt && counter && status != OrderStatus.Paid
                    ? status == OrderStatus.Pending ? PaymentStatus.Pending : PaymentStatus.Cancelled : (PaymentStatus?)null
            where order.Purchaser.GymId == gymId && (order.Items.Any() || isPt) &&
                (payment == null || payment.LockerRentalContractId == null && payment.SourcePaymentTransactionId == null)
            select new
            {
                Order = order, Pt = pt, Payment = payment, IsPt = isPt, Status = status, Method = method, PaymentStatus = paymentStatus,
                Gym = order.Items.Any(item => item.Package.Components.Any(component => component.ComponentType == ComponentType.GYM)),
                Yoga = order.Items.Any(item => item.Package.Components.Any(component => component.ComponentType == ComponentType.YOGA)),
                HasPt = isPt || order.Items.Any(item => item.Package.Components.Any(component => component.ComponentType == ComponentType.PT))
            };

        if (filter.Search is { } search)
        {
            var key = search.ToUpperInvariant();
            query = query.Where(row => row.Order.OrderNumber.ToUpper().Contains(key) ||
                row.Order.Purchaser.FullName.ToUpper().Contains(key) || row.Order.Purchaser.PhoneNumber.Contains(search) ||
                row.Order.Items.Any(item => item.PackageNameSnapshot.ToUpper().Contains(key)) ||
                row.Pt != null && row.Pt.PackageNameSnapshot != null && row.Pt.PackageNameSnapshot.ToUpper().Contains(key));
        }
        if (filter.Status is { } statusFilter) query = query.Where(row => row.Status == statusFilter);
        query = filter.ServiceType switch
        {
            ComponentType.GYM => query.Where(row => row.Gym),
            ComponentType.YOGA => query.Where(row => row.Yoga),
            ComponentType.PT => query.Where(row => row.HasPt),
            _ => query
        };
        if (filter.WithoutMethod) query = query.Where(row => row.Method == null);
        else if (filter.Method is { } methodFilter) query = query.Where(row => row.Method == methodFilter);
        if (filter.WithoutPaymentStatus) query = query.Where(row => row.PaymentStatus == null);
        else if (filter.PaymentStatus is { } paymentFilter) query = query.Where(row => row.PaymentStatus == paymentFilter);
        if (filter.FromUtc is { } from) query = query.Where(row => row.Order.CreatedAt >= from);
        if (filter.BeforeUtc is { } before) query = query.Where(row => row.Order.CreatedAt < before);

        var total = await query.CountAsync(ct);
        var sorted = filter.NewestFirst ? query.OrderByDescending(row => row.Order.CreatedAt)
            : query.OrderByDescending(row => row.Status == OrderStatus.Pending).ThenByDescending(row => row.Order.CreatedAt);
        var page = await sorted.ThenByDescending(row => row.Order.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(row => new
            {
                row.Order.Id, row.Order.OrderNumber, row.Order.Purchaser.FullName, row.Order.CreatedAt,
                row.IsPt, row.Gym, row.Yoga, row.HasPt, row.Status, row.Method, row.PaymentStatus,
                HasPayment = row.Payment != null,
                AmountDue = row.Payment != null ? (decimal?)row.Payment.AmountDue : row.Pt != null ? row.Pt.PurchasePrice : null,
                AmountReceived = row.Payment != null ? (decimal?)row.Payment.AmountReceived : null,
                PaidAt = row.Payment != null ? row.Payment.PaidAt : null,
                InvoiceNumber = row.Payment != null && row.Payment.Invoice != null ? row.Payment.Invoice.InvoiceNumber : null,
                RefundStatus = row.Payment != null ? row.Payment.RefundStatus : null,
                PtStatus = row.Pt != null ? (PTPurchaseStatus?)row.Pt.Status : null,
                HoldUntil = row.Pt != null ? row.Pt.HoldExpiresAt : null,
                PtPackageName = row.Pt != null ? row.Pt.PackageNameSnapshot : null
            }).ToListAsync(ct);

        if (page.Count == 0)
            return new(filter.Page, filter.PageSize, total, []);

        var ids = page.Select(row => row.Id).ToArray();
        var orderItems = (await context.OrderItems.AsNoTracking().Where(item => ids.Contains(item.OrderId))
            .OrderBy(item => item.Id)
            .Select(item => new { item.OrderId, item.PackageNameSnapshot, item.UnitPriceSnapshot }).ToListAsync(ct))
            .ToLookup(item => item.OrderId, item => new StaffPackageOrderItemResponse(item.PackageNameSnapshot, item.UnitPriceSnapshot));

        var rows = page.Select(row =>
        {
            var items = orderItems[row.Id].ToArray();
            if (items.Length == 0 && row.IsPt && row.AmountDue is { } ptAmount)
                items = [new(row.PtPackageName ?? "Gói PT", ptAmount)];
            var services = new List<string>(3);
            if (row.Gym) services.Add(nameof(ComponentType.GYM));
            if (row.Yoga) services.Add(nameof(ComponentType.YOGA));
            if (row.HasPt) services.Add(nameof(ComponentType.PT));

            var deadline = row.Method == PaymentMethod.Cash
                ? row.HoldUntil ?? row.CreatedAt.AddHours(PackageOrderTiming.CounterPaymentHours)
                : row.CreatedAt.AddMinutes(PackageOrderTiming.OnlinePaymentMinutes);
            var canConfirm = row.Status == OrderStatus.Pending && row.Method == PaymentMethod.Cash &&
                row.PaymentStatus == PaymentStatus.Pending && row.AmountDue > 0 &&
                (row.IsPt ? !row.HasPayment && row.PtStatus == PTPurchaseStatus.PendingPayment && row.HoldUntil > nowUtc : row.HasPayment);

            return new StaffPackageOrderResponse(row.Id, row.OrderNumber, row.FullName, StaffOrderUtc(row.CreatedAt),
                StaffOrderUtc(deadline), row.Status.ToString(), row.PaymentStatus?.ToString(), row.Method?.ToString(),
                row.AmountDue, row.AmountReceived, row.PaidAt is { } paidAt ? StaffOrderUtc(paidAt) : null,
                row.InvoiceNumber, row.RefundStatus, row.IsPt, canConfirm, services, items);
        }).ToArray();

        return new(filter.Page, filter.PageSize, total, rows);
    }

    private static DateTimeOffset StaffOrderUtc(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
