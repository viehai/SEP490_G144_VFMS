using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed partial class LegacySchemaMigrationTests
{
    [SqlServerMigrationFact]
    public async Task TransactionProjectionExecutesOnSqlServerWithOwnedSnapshotsAndNoTrackedOrPersistedChanges()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await using var context = database.Context();
        await context.Database.MigrateAsync();
        var gym = new Gym { GymName = "Transaction projection test gym" };
        var owner = new ApplicationUser
        {
            UserId = "transaction-owner", FullName = "Owner", Email = "owner@example.test", PhoneNumber = "0900000032",
            PasswordHash = "test", Gym = gym, AccountStatus = AccountStatus.Active
        };
        var other = new ApplicationUser
        {
            UserId = "transaction-other", FullName = "Other", Email = "other@example.test", PhoneNumber = "0900000033",
            PasswordHash = "test", Gym = gym, AccountStatus = AccountStatus.Active
        };
        var package = new Package
        {
            Name = "Mutable plan", Price = 900_000m, PackageStatus = PackageStatus.Active,
            Components = [new PackageComponent { ComponentType = ComponentType.GYM, DurationDays = 7 }]
        };
        var voucher = new Voucher
        {
            Code = "MUTABLE", Name = "Mutable voucher", DiscountType = DiscountType.FIXED, DiscountValue = 1m,
            CreatedByUser = owner, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(1)
        };
        var paidAt = new DateTime(2026, 11, 14, 5, 0, 0, DateTimeKind.Utc);
        var owned = new Order
        {
            OrderNumber = "SQL-OWNED-TRANSACTION", Purchaser = owner, Channel = OrderChannel.Online,
            OrderStatus = OrderStatus.Paid, CompletedAt = paidAt,
            Items = [new OrderItem
            {
                Package = package, Beneficiary = owner, PackageNameSnapshot = "Purchased plan",
                UnitPriceSnapshot = 500_000m, ActivationStartDate = new DateOnly(2026, 11, 15),
                ComponentTermsSnapshot = "Purchased immutable terms"
            }],
            PaymentTransaction = new PaymentTransaction
            {
                User = owner, GatewayName = "SePay", PaymentMethod = PaymentMethod.VietQR,
                PaymentStatus = PaymentStatus.Paid, AmountDue = 400_000m, AmountReceived = 400_000m, PaidAt = paidAt,
                Invoice = new Invoice { InvoiceNumber = "SQL-OWNED-INVOICE", IssuedAt = paidAt }
            },
            VoucherRedemption = new VoucherRedemption
            {
                Voucher = voucher, VoucherCodeSnapshot = "PAST20", VoucherNameSnapshot = "Purchased campaign",
                DiscountAmount = 100_000m, RedemptionStatus = VoucherRedemptionStatus.Confirmed, RedeemedAt = paidAt
            }
        };
        var foreign = new Order
        {
            OrderNumber = "SQL-FOREIGN-TRANSACTION", Purchaser = other, Channel = OrderChannel.Counter,
            Items = [new OrderItem
            {
                Package = package, Beneficiary = owner, PackageNameSnapshot = "Another purchaser's plan", UnitPriceSnapshot = 700_000m
            }],
            PaymentTransaction = new PaymentTransaction
            {
                User = other, GatewayName = "Cash", PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending, AmountDue = 700_000m
            }
        };
        context.Orders.AddRange(owned, foreign);
        await context.SaveChangesAsync();
        var ownedId = owned.Id;
        var foreignId = foreign.Id;
        var orderUpdatedAt = owned.UpdatedAt;
        var paymentUpdatedAt = owned.PaymentTransaction!.UpdatedAt;
        var redemptionUpdatedAt = owned.VoucherRedemption!.UpdatedAt;
        context.ChangeTracker.Clear();

        var repository = new MembershipTransactionRepository(context);
        var service = new MembershipTransactionService(repository);
        var page = await repository.ListAsync(owner.UserId, 1, 1, CancellationToken.None);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(ownedId, Assert.Single(page.Items).OrderId);
        Assert.Empty((await repository.ListAsync(owner.UserId, 2, 1, CancellationToken.None)).Items);
        Assert.Null(await repository.FindAsync(owner.UserId, foreignId, CancellationToken.None));
        Assert.Null(await repository.FindAsync(owner.UserId, int.MaxValue, CancellationToken.None));
        var projection = await repository.FindAsync(owner.UserId, ownedId, CancellationToken.None);
        Assert.NotNull(projection);
        Assert.Equal("Purchased plan", Assert.Single(projection.Items).PlanName);
        Assert.Equal(500_000m, projection.Items[0].UnitPrice);
        Assert.Equal("PAST20", projection.Voucher!.Code);
        Assert.Equal(100_000m, projection.Voucher.DiscountAmount);
        Assert.Equal("SQL-OWNED-INVOICE", projection.Invoice!.InvoiceNumber);
        var response = await service.GetAsync(owner.UserId, ownedId, CancellationToken.None);
        Assert.True(response.IsSuccess);
        Assert.Equal(500_000m, response.Data!.Subtotal);
        Assert.Equal(100_000m, response.Data.DiscountAmount);
        Assert.Equal(400_000m, response.Data.Transaction.AmountDue);
        Assert.Equal(400_000m, response.Data.Transaction.AmountReceived);
        Assert.Empty(context.ChangeTracker.Entries());

        var reread = await repository.FindAsync(owner.UserId, ownedId, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(reread));
        Assert.Equal(orderUpdatedAt, await context.Orders.Where(row => row.Id == ownedId).Select(row => row.UpdatedAt).SingleAsync());
        Assert.Equal(paymentUpdatedAt, await context.PaymentTransactions.Where(row => row.OrderId == ownedId).Select(row => row.UpdatedAt).SingleAsync());
        Assert.Equal(redemptionUpdatedAt, await context.VoucherRedemptions.Where(row => row.OrderId == ownedId).Select(row => row.UpdatedAt).SingleAsync());
        Assert.Equal(1, await context.Invoices.CountAsync());
        Assert.Empty(await context.AuditLogs.ToListAsync());
        Assert.Empty(await context.MemberMemberships.ToListAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
