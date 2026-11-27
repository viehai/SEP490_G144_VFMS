using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

// UC 4.1, 4.2 and 4.4: authorization, concurrency and "preview reserves nothing".
public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task PTPublishByANonTrainerIsForbidden()
    {
        await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var response = await member.PutAsJsonAsync("/api/pt/availability",
            new PTPublishRequest([new(DayOfWeek.Monday, new(10, 0))]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PTPreviewReservesNothingAndCreatesNoOrder()
    {
        var request = await PreparePTCheckout();
        int orders, purchases, sessions;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (orders, purchases, sessions) = (await db.Orders.CountAsync(), await db.PTPurchases.CountAsync(), await db.PTSessions.CountAsync());
        }

        using var member = Client("member", "Member");
        using var preview = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.NotEmpty((await preview.Content.ReadFromJsonAsync<PTBookingPreview>())!.Sessions);

        await using var after = _app.Services.CreateAsyncScope();
        var afterDb = after.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(orders, await afterDb.Orders.CountAsync());
        Assert.Equal(purchases, await afterDb.PTPurchases.CountAsync());
        Assert.Equal(sessions, await afterDb.PTSessions.CountAsync());
        // The same schedule is still free for another buyer.
        using var other = Client("gift", "Customer");
        using var hold = await other.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
    }

    // The shared in-memory SQLite connection cannot serve two requests at once, so the races below run the services
    // on independent connections to a copy of the database (the same approach as the voucher races).
    [Fact]
    public async Task PTPublishRunInParallelKeepsOneConsistentTemplate()
    {
        await PreparePTCheckout();
        PTSlotInput[] slots = [new(DayOfWeek.Monday, new(10, 0)), new(DayOfWeek.Tuesday, new(10, 0)), new(DayOfWeek.Saturday, new(8, 0))];
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunPTRaceAsync(connectionString, start, service => service.Publish("buyer", new(slots), CancellationToken.None)),
                RunPTRaceAsync(connectionString, start, service => service.Publish("buyer", new(slots), CancellationToken.None)));
            // GBR-09: at least one publish wins and a loser is refused cleanly, never half applied.
            Assert.Contains(outcomes, result => result.IsSuccess);
            Assert.All(outcomes, result => Assert.True(result.IsSuccess || result.StatusCode == 409, result.ErrorMessage));
            await using var verify = VoucherRaceContext(connectionString);
            var rows = await verify.PTAvailabilities.Where(a => a.PTUserId == "buyer").ToListAsync();
            Assert.Equal(slots.Length, rows.Count);
            Assert.Equal(rows.Count, rows.Select(a => (a.DayOfWeek, a.StartTime)).Distinct().Count());
        });
    }

    [Fact]
    public async Task PTTwoBuyersHoldingTheSameSlotsAtOnceGetExactlyOneHold()
    {
        var request = await PreparePTCheckout(3);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunPTRaceAsync(connectionString, start, service => service.Checkout("member", request, CancellationToken.None)),
                RunPTRaceAsync(connectionString, start, service => service.Checkout("gift", request, CancellationToken.None)));
            Assert.Single(outcomes, result => result.IsSuccess);
            Assert.Single(outcomes, result => !result.IsSuccess && result.StatusCode == 409);
            await using var verify = VoucherRaceContext(connectionString);
            Assert.Equal(1, await verify.PTPurchases.CountAsync());
            Assert.Equal(3, await verify.PTSessions.CountAsync(s => s.SessionStatus == PTSessionStatus.CheckoutHold));
        });
    }

    [Fact]
    public async Task PTDuplicateCallbacksArrivingTogetherActivateTheContractOnce()
    {
        var request = await PreparePTCheckout(3);
        using var member = Client("member", "Member");
        var checkout = await IssueQr(member, await HoldPT(member, request));
        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        var callback = SignedPTCallback(checkout, 4242, transferAt);
        await WithVoucherRaceDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var outcomes = await Task.WhenAll(
                RunPTRaceAsync(connectionString, start, async (service, membership) =>
                    await membership.ConfirmSepayAsync("", callback.Timestamp, callback.Signature, callback.Body, CancellationToken.None)),
                RunPTRaceAsync(connectionString, start, async (service, membership) =>
                    await membership.ConfirmSepayAsync("", callback.Timestamp, callback.Signature, callback.Body, CancellationToken.None)));
            Assert.All(outcomes, result => Assert.True(result.IsSuccess, result.ErrorMessage));
            await using var verify = VoucherRaceContext(connectionString);
            Assert.Equal(PTPurchaseStatus.Active, (await verify.PTPurchases.SingleAsync()).Status);
            Assert.Equal(3, await verify.PTSessions.CountAsync(s => s.SessionStatus == PTSessionStatus.Scheduled));
            Assert.Equal(1, await verify.PaymentTransactions.CountAsync(p => p.GatewayTransactionReference == "4242"));
            Assert.Equal(1, await verify.Invoices.CountAsync());
        });
    }

    private Task<T> RunPTRaceAsync<T>(string connectionString, Barrier start, Func<PersonalTrainingService, Task<T>> work) =>
        RunPTRaceAsync(connectionString, start, (service, _) => work(service));

    private Task<T> RunPTRaceAsync<T>(string connectionString, Barrier start,
        Func<PersonalTrainingService, MembershipPurchaseService, Task<T>> work) => Task.Run(async () =>
    {
        await using var context = VoucherRaceContext(connectionString);
        await using var scope = _app.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IMembershipPaymentGateway>();
        var payments = new MembershipPurchaseRepository(context);
        var service = new PersonalTrainingService(new PersonalTrainingRepository(context), _clock, gateway, payments,
            scope.ServiceProvider.GetRequiredService<IBannedWordService>());
        var membership = new MembershipPurchaseService(payments, gateway, _clock, service);
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "PT race workers did not reach the start barrier.");
        return await work(service, membership);
    });

    private (string Timestamp, string Signature, byte[] Body) SignedPTCallback(PTCheckoutResponse order, long id, DateTimeOffset at)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, transactionDate = at.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            accountNumber = BankAccount, content = $"BANK {order.VietQr!.TransferContent} PAYMENT", transferType = "in",
            transferAmount = order.AmountDue
        });
        var timestamp = _clock.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), prefix.Concat(body).ToArray());
        return (timestamp, $"sha256={Convert.ToHexString(signature).ToLowerInvariant()}", body);
    }
}
