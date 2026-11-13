using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task VoucherConcurrentLastQuotaApplicationsOnIndependentConnectionsReserveAtMostOneSlot()
    {
        using var buyer = Client("buyer", "Customer");
        using var member = Client("member", "Member");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(member, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(quota: 1);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunVoucherRaceAsync(connectionString, start, service => service.ApplyVoucherAsync("buyer", first.OrderNumber, new("SAVE20"), CancellationToken.None)),
                RunVoucherRaceAsync(connectionString, start, service => service.ApplyVoucherAsync("member", second.OrderNumber, new("save20"), CancellationToken.None)));
            Assert.Single(outcomes, result => result.IsSuccess);
            Assert.Single(outcomes, result => !result.IsSuccess && result.StatusCode == 409);
            await using var verify = VoucherRaceContext(connectionString);
            Assert.Single(await verify.VoucherRedemptions.Where(row => row.RedemptionStatus == VoucherRedemptionStatus.Reserved).ToListAsync());
            Assert.Equal(1, await verify.PaymentTransactions.CountAsync(row => row.AmountDue == 400000));
            Assert.Equal(1, await verify.PaymentTransactions.CountAsync(row => row.AmountDue == 500000));
        });
    }

    [Fact]
    public async Task VoucherConcurrentDuplicateApplyOnIndependentConnectionsIsIdempotent()
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(quota: 1);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunVoucherRaceAsync(connectionString, start, service => service.ApplyVoucherAsync("buyer", order.OrderNumber, new("SAVE20"), CancellationToken.None)),
                RunVoucherRaceAsync(connectionString, start, service => service.ApplyVoucherAsync("buyer", order.OrderNumber, new(" save20 "), CancellationToken.None)));
            Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess, outcome.ErrorMessage));
            await using var verify = VoucherRaceContext(connectionString);
            Assert.Single(await verify.VoucherRedemptions.ToListAsync());
            Assert.Equal(400000m, (await verify.PaymentTransactions.SingleAsync()).AmountDue);
        });
    }

    [Fact]
    public async Task VoucherConcurrentDuplicateSePayCallbacksConfirmAndFulfillExactlyOnce()
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, order);
        var callback = SignedVoucherCallback(applied, 19201);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunVoucherRaceAsync(connectionString, start, service => service.ConfirmSepayAsync("", callback.Timestamp, callback.Signature, callback.Body, CancellationToken.None)),
                RunVoucherRaceAsync(connectionString, start, service => service.ConfirmSepayAsync("", callback.Timestamp, callback.Signature, callback.Body, CancellationToken.None)));
            Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess, outcome.ErrorMessage));
            await using var verify = VoucherRaceContext(connectionString);
            Assert.Equal(VoucherRedemptionStatus.Confirmed, (await verify.VoucherRedemptions.SingleAsync()).RedemptionStatus);
            Assert.Equal(OrderStatus.Paid, (await verify.Orders.SingleAsync()).OrderStatus);
            Assert.Single(await verify.MemberMemberships.ToListAsync());
            Assert.Single(await verify.Invoices.ToListAsync());
        });
    }

    [Fact]
    public async Task VoucherPaymentTimeoutRaceHasOneConsistentTerminalOutcomeAcrossConnections()
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, order);
        _clock.Now = order.ExpiresAt;
        var callback = SignedVoucherCallback(applied, 19202);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var confirmation = RunVoucherRaceAsync(connectionString, start,
                service => service.ConfirmSepayAsync("", callback.Timestamp, callback.Signature, callback.Body, CancellationToken.None));
            var expiration = RunVoucherRaceAsync(connectionString, start, async service =>
            {
                await service.ExpireOrdersAsync(CancellationToken.None);
                return true;
            });
            await Task.WhenAll(confirmation, expiration);
            var result = await confirmation;
            await using var verify = VoucherRaceContext(connectionString);
            var redemption = await verify.VoucherRedemptions.SingleAsync();
            var stored = await verify.Orders.SingleAsync();
            var payment = await verify.PaymentTransactions.SingleAsync();
            if (result.IsSuccess)
            {
                Assert.Equal(VoucherRedemptionStatus.Confirmed, redemption.RedemptionStatus);
                Assert.Equal(OrderStatus.Paid, stored.OrderStatus);
                Assert.Equal(PaymentStatus.Paid, payment.PaymentStatus);
                Assert.Single(await verify.MemberMemberships.ToListAsync());
                Assert.Single(await verify.Invoices.ToListAsync());
            }
            else
            {
                Assert.Equal(409, result.StatusCode);
                Assert.Equal(VoucherRedemptionStatus.Released, redemption.RedemptionStatus);
                Assert.Equal(OrderStatus.Expired, stored.OrderStatus);
                Assert.Equal(PaymentStatus.Cancelled, payment.PaymentStatus);
                Assert.Empty(await verify.MemberMemberships.ToListAsync());
                Assert.Empty(await verify.Invoices.ToListAsync());
            }
        });
    }

    private async Task WithVoucherRaceDatabaseAsync(Func<string, Task> work)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vfms-voucher-race-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 30, ForeignKeys = true
        }.ToString();
        try
        {
            await using (var target = new SqliteConnection(connectionString))
            {
                await target.OpenAsync();
                _connection.BackupDatabase(target);
            }
            await work(connectionString);
        }
        finally { File.Delete(path); }
    }

    private Task<T> RunVoucherRaceAsync<T>(string connectionString, Barrier start,
        Func<IMembershipPurchaseService, Task<T>> work) => Task.Run(async () =>
    {
        await using var context = VoucherRaceContext(connectionString);
        await using var scope = _app.Services.CreateAsyncScope();
        var service = new MembershipPurchaseService(new MembershipPurchaseRepository(context),
            scope.ServiceProvider.GetRequiredService<IMembershipPaymentGateway>(), _clock);
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Voucher race workers did not reach the start barrier.");
        return await work(service);
    });

    private static TestContext VoucherRaceContext(string connectionString) =>
        new(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connectionString).Options);

    private (string Timestamp, string Signature, byte[] Body) SignedVoucherCallback(MembershipPurchaseResponse order, long id)
    {
        var date = order.CreatedAt.AddSeconds(1).ToOffset(TimeSpan.FromHours(7));
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, transactionDate = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            accountNumber = BankAccount, content = $"BANK {order.VietQr!.TransferContent} PAYMENT", transferType = "in",
            transferAmount = order.AmountDue
        });
        var timestamp = _clock.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), prefix.Concat(body).ToArray());
        return (timestamp, $"sha256={Convert.ToHexString(signature).ToLowerInvariant()}", body);
    }
}
