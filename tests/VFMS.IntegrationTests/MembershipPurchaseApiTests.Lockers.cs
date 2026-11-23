using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Application.Interfaces;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private static readonly object LockerHandover = new { termsAccepted = true, identityVerified = true };

    [Theory]
    [InlineData("no-worker", true)]
    [InlineData("grace", true)]
    [InlineData("expired", true)]
    [InlineData("reallocated", false)]
    [InlineData("cancelled", false)]
    public async Task LockerTimelyTransferWithDelayedCallbackRechecksAllocation(string situation, bool fulfilled)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        var request = new { lockerId, policyId };
        var checkout = await CreateLockerCheckout(member, "/api/lockers/rentals", request);
        var number = checkout.GetProperty("orderNumber").GetString();
        var deadline = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime());
        var transferAt = deadline.AddMinutes(-1);
        if (situation == "cancelled")
        {
            _clock.Now = transferAt.AddSeconds(1);
            using var cancelled = await member.PostAsync($"/api/lockers/orders/{number}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        }

        _clock.Now = deadline.AddMinutes(situation == "grace" ? 2 : 6);
        if (situation is "expired" or "reallocated")
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILockerService>().Expire(CancellationToken.None);
        }
        string? replacement = null;
        if (situation == "reallocated")
        {
            var next = await CreateLockerCheckout(member, "/api/lockers/rentals", request);
            replacement = next.GetProperty("orderNumber").GetString();
        }
        if (situation == "grace")
        {
            var waiting = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{number}");
            Assert.Equal("AwaitingConfirmation", waiting.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, waiting.GetProperty("vietQr").ValueKind);
            using var blocked = await member.PostAsJsonAsync("/api/lockers/rentals", request);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        }

        var code = checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var amount = checkout.GetProperty("amountDue").GetDecimal();
        using var callback = await NotifyTransferAsync(code, amount, 913, transferAt);
        Assert.True(callback.StatusCode == HttpStatusCode.OK, await callback.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(code, amount, 913, transferAt);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var order = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{number}");
        Assert.Equal(fulfilled ? "Paid" : "RefundPending", order.GetProperty("status").GetString());
        var refunds = await staff.GetFromJsonAsync<JsonElement>("/api/lockers/staff/refunds");
        Assert.Equal(fulfilled ? 0 : 1, refunds.GetArrayLength());
        if (fulfilled)
        {
            var rental = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
            Assert.Equal("PendingHandover", rental.GetProperty("status").GetString());
        }
        if (replacement is not null)
        {
            var next = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{replacement}");
            Assert.Equal("Pending", next.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task LockerVietQrUsesSharedWebhookAndWaitsForStaffHandover()
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        using var outsider = Client("other-staff", "Staff");
        var request = new { lockerId, policyId };
        using var hold = await member.PostAsJsonAsync("/api/lockers/rentals", request);
        Assert.True(hold.StatusCode == HttpStatusCode.Created, await hold.Content.ReadAsStringAsync());
        var checkout = await hold.Content.ReadFromJsonAsync<JsonElement>();
        var rentalId = checkout.GetProperty("rentalId").GetInt32();
        var qr = checkout.GetProperty("vietQr").Deserialize<VietQrPaymentResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var amount = checkout.GetProperty("amountDue").GetDecimal();
        using var duplicate = await member.PostAsJsonAsync("/api/lockers/rentals", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var transfer = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime()).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(qr.TransferContent, amount, 910, transfer);
        Assert.True(paid.StatusCode == HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(qr.TransferContent, amount, 910, transfer);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var extra = await NotifyTransferAsync(qr.TransferContent, amount, 912, transfer);
        Assert.Equal(HttpStatusCode.OK, extra.StatusCode);
        using var repeatedExtra = await NotifyTransferAsync(qr.TransferContent, amount, 912, transfer);
        Assert.Equal(HttpStatusCode.OK, repeatedExtra.StatusCode);
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal("PendingHandover", mine.GetProperty("status").GetString());
        using var wrongGym = await outsider.PostAsJsonAsync($"/api/lockers/staff/rentals/{rentalId}/handover", LockerHandover);
        Assert.Equal(HttpStatusCode.NotFound, wrongGym.StatusCode);
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{rentalId}/handover", LockerHandover);
        Assert.True(handover.StatusCode == HttpStatusCode.OK, await handover.Content.ReadAsStringAsync());
        using var retried = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{rentalId}/handover", LockerHandover);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        mine = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal("Active", mine.GetProperty("status").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Equal(1, await db.MemberMemberships.CountAsync());
        Assert.Equal(1, await db.PaymentTransactions.CountAsync(p => p.LockerPurpose == LockerPaymentPurpose.UnappliedTransfer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerLateTransferRequestsARefundButUncollectedMonthlyRentalStartsAutomatically(bool latePayment)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        var checkout = await CreateLockerCheckout(member, "/api/lockers/rentals", new { lockerId, policyId });
        var deadline = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime());
        var transfer = latePayment ? deadline.AddSeconds(1) : deadline.AddMinutes(-14);
        var orderNumber = checkout.GetProperty("orderNumber").GetString();
        var amount = checkout.GetProperty("amountDue").GetDecimal();
        var transferContent = checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(transferContent, amount, 911, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        if (!latePayment)
        {
            var start = new DateTimeOffset(Today.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
            _clock.Now = start.AddDays(1);
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILockerService>().Expire(CancellationToken.None);
            var rental = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
            Assert.Equal("PendingHandover", rental.GetProperty("status").GetString());
            var id = rental.GetProperty("id").GetInt32();
            var expiry = rental.GetProperty("expiresAt").GetDateTime();
            Assert.Equal(start.UtcDateTime.AddMonths(1), expiry);
            using var handedOver = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover", LockerHandover);
            Assert.Equal(HttpStatusCode.OK, handedOver.StatusCode);
            var after = await handedOver.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expiry, after.GetProperty("expiresAt").GetDateTime());
            Assert.Empty((await member.GetFromJsonAsync<JsonElement>("/api/checkout-refunds/mine")).EnumerateArray());
            return;
        }
        var grid = await member.GetFromJsonAsync<JsonElement>("/api/lockers");
        Assert.Equal("Available", Assert.Single(grid.EnumerateArray()).GetProperty("status").GetString());
        var refunds = await staff.GetFromJsonAsync<JsonElement>("/api/lockers/staff/refunds");
        var pending = Assert.Single(refunds.EnumerateArray());
        Assert.Equal(amount, pending.GetProperty("refundAmount").GetDecimal());
        var automatic = Assert.Single((await member.GetFromJsonAsync<JsonElement>("/api/checkout-refunds/mine")).EnumerateArray());
        Assert.Equal(amount, automatic.GetProperty("amount").GetDecimal());
        Assert.Equal("Pending", automatic.GetProperty("status").GetString());
        Assert.Equal(orderNumber, automatic.GetProperty("orderNumber").GetString());
        var refundPath = $"/api/checkout-refunds/{automatic.GetProperty("id").GetInt32()}/outcome";
        using var failed = await staff.PostAsJsonAsync(refundPath, new { succeeded = false });
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        var failedRefund = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", failedRefund.GetProperty("status").GetString());
        using var completed = await staff.PostAsJsonAsync(refundPath, new { succeeded = true, reference = "TRANSFER-REFUND" });
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var result = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Refunded", result.GetProperty("status").GetString());
        using var replay = await NotifyTransferAsync(transferContent, amount, 911, transfer);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var rest = await staff.GetFromJsonAsync<JsonElement>("/api/lockers/staff/refunds");
        Assert.Empty(rest.EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerDailyCashSettlesDepositUsingRecordedKeyOutcome(bool keyLost)
    {
        var (lockerId, _) = await PrepareLocker();
        _clock.Now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(7));
        using var manager = Client("manager", "Manager");
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        using var policy = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            dailyFee = 20_000,
            dailyDeposit = 100_000,
            oneMonth = 200_000,
            threeMonths = 550_000,
            sixMonths = 1_000_000,
            twelveMonths = 1_900_000,
            overdueDailyFee = 10_000,
            keyReplacementFee = 100_000
        });
        Assert.True(policy.StatusCode == HttpStatusCode.OK, await policy.Content.ReadAsStringAsync());
        var tiers = await policy.Content.ReadFromJsonAsync<JsonElement>();
        var policyId = Assert.Single(tiers.EnumerateArray(), t => t.GetProperty("type").GetString() == "Daily").GetProperty("id").GetInt32();
        using var online = await member.PostAsJsonAsync("/api/lockers/rentals", new { lockerId, policyId });
        Assert.Equal(HttpStatusCode.BadRequest, online.StatusCode);
        var checkout = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var rentalId = checkout.GetProperty("rentalId").GetInt32();
        Assert.Equal(120_000, checkout.GetProperty("amountDue").GetDecimal());
        var rentalPath = $"/api/lockers/staff/rentals/{rentalId}";
        using var handover = await staff.PostAsJsonAsync($"{rentalPath}/handover",
            new { termsAccepted = true, identityVerified = true, cashAmount = 120_000 });
        Assert.True(handover.StatusCode == HttpStatusCode.OK, await handover.Content.ReadAsStringAsync());
        var rental = await handover.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_clock.Now.UtcDateTime.Date.AddHours(15), rental.GetProperty("expiresAt").GetDateTime());
        Assert.Equal(100_000, rental.GetProperty("depositHeld").GetDecimal());
        if (keyLost)
        {
            using var confirmed = await staff.PostAsync($"{rentalPath}/key-loss", null);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            var quote = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100_000, quote.GetProperty("forfeitedDeposit").GetDecimal());
            Assert.Equal(0, quote.GetProperty("additionalAmountDue").GetDecimal());
        }
        else
        {
            _clock.Now = new DateTimeOffset(rental.GetProperty("expiresAt").GetDateTime()).AddSeconds(1);
            var outstanding = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
            Assert.True(outstanding.GetProperty("dailyKeyOutstanding").GetBoolean());
            Assert.Equal(100_000, outstanding.GetProperty("depositHeld").GetDecimal());
        }
        var close = new { lockerEmpty = true, lockSafe = true, refundMethod = "Cash" };
        using var terminate = await staff.PostAsJsonAsync($"{rentalPath}/terminate", close);
        Assert.True(terminate.StatusCode == HttpStatusCode.OK, await terminate.Content.ReadAsStringAsync());
        var closed = await terminate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Terminated", closed.GetProperty("status").GetString());
        Assert.Equal(keyLost ? 0 : 100_000, closed.GetProperty("refundedDeposit").GetDecimal());
        using var retry = await staff.PostAsJsonAsync($"{rentalPath}/terminate", close);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(1, await db.PaymentTransactions.CountAsync(p => p.LockerRentalContractId == rentalId));
        Assert.Equal(keyLost ? LockerStatus.Maintenance : LockerStatus.Available, (await db.Lockers.SingleAsync()).LockerStatus);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.NewValue!.StartsWith("LockerTerminated:")));
    }

    private static async Task<JsonElement> CreateLockerCheckout(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task LockerRenewalKeepsTheSameLockerAndClearanceRequiresManagerVerification()
    {
        var (lockerId, policyId) = await PrepareLocker();
        var firstOfMonth = new DateOnly(Today.Year, Today.Month, 1);
        var endOfMonth = Enumerable.Range(0, 5).Select(firstOfMonth.AddMonths)
            .Select(d => d.AddMonths(1).AddDays(-1))
            .First(d => d.Day > DateTime.DaysInMonth(d.AddMonths(1).Year, d.AddMonths(1).Month));
        _clock.Now = new DateTimeOffset(endOfMonth.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(7));
        using var staff = Client("staff", "Staff");
        using var manager = Client("manager", "Manager");
        using var member = Client("member", "Member");
        var initial = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = initial.GetProperty("rentalId").GetInt32();
        var staffPath = $"/api/lockers/staff/rentals/{id}";
        var initialNumber = initial.GetProperty("orderNumber").GetString();
        using var paid = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{initialNumber}/cash", new { amount = 250_000 });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        using var handedOver = await staff.PostAsJsonAsync($"{staffPath}/handover", LockerHandover);
        Assert.True(handedOver.StatusCode == HttpStatusCode.OK, await handedOver.Content.ReadAsStringAsync());
        var original = await handedOver.Content.ReadFromJsonAsync<JsonElement>();
        var originalExpiry = original.GetProperty("expiresAt").GetDateTime();
        Assert.Equal(new DateTimeOffset(endOfMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime,
            originalExpiry);
        int replacementId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var zone = await db.LockerZones.SingleAsync();
            var replacement = new Locker { LockerNumber = "A02", LockerZone = zone };
            db.Lockers.Add(replacement);
            await db.SaveChangesAsync();
            replacementId = replacement.Id;
        }
        using var broken = await staff.PutAsJsonAsync($"/api/lockers/staff/grid/{lockerId}/maintenance",
            new { maintenance = true });
        Assert.Equal(HttpStatusCode.OK, broken.StatusCode);
        using var select = await member.PostAsJsonAsync($"/api/lockers/rentals/{id}/replacement", new { lockerId = replacementId });
        Assert.True(select.StatusCode == HttpStatusCode.OK, await select.Content.ReadAsStringAsync());
        var grid = await member.GetFromJsonAsync<JsonElement>("/api/lockers");
        Assert.Equal("Reserved", Assert.Single(grid.EnumerateArray(), slot => slot.GetProperty("id").GetInt32() == replacementId)
            .GetProperty("status").GetString());
        using var relocate = await staff.PostAsJsonAsync($"{staffPath}/replacement/handover", new { lockerId = replacementId });
        Assert.True(relocate.StatusCode == HttpStatusCode.OK, await relocate.Content.ReadAsStringAsync());
        var relocated = await relocate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, relocated.GetProperty("id").GetInt32());
        Assert.Equal(originalExpiry, relocated.GetProperty("expiresAt").GetDateTime());
        Assert.Equal(replacementId, relocated.GetProperty("locker").GetProperty("id").GetInt32());

        _clock.Now = new DateTimeOffset(originalExpiry).AddMinutes(-2);
        var renewal = await CreateLockerCheckout(member, $"/api/lockers/rentals/{id}/renew", new { policyId });
        var renewalCode = renewal.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var amountDue = renewal.GetProperty("amountDue").GetDecimal();
        Assert.Equal(250_000, amountDue);
        var transferred = _clock.Now.AddMinutes(1);
        _clock.Now = _clock.Now.AddMinutes(3);
        using var renewed = await NotifyTransferAsync(renewalCode, amountDue, 915, transferred);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal(replacementId, mine.GetProperty("locker").GetProperty("id").GetInt32());
        var renewedExpiry = mine.GetProperty("expiresAt").GetDateTime();
        Assert.Equal(originalExpiry.AddMonths(1), renewedExpiry);
        _clock.Now = new DateTimeOffset(renewedExpiry).AddDays(7).AddSeconds(-1);
        var inventory = new { inventory = new[] { new { name = "Khăn", quantity = 1 } } };
        using var tooEarly = await staff.PostAsJsonAsync($"{staffPath}/clearance", inventory);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        _clock.Now = _clock.Now.AddSeconds(1);
        using var requested = await staff.PostAsJsonAsync($"{staffPath}/clearance", inventory);
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var incidentId = await requested.Content.ReadFromJsonAsync<int>();
        var verify = $"/api/lockers/manager/clearances/{incidentId}/verify";
        using var forbidden = await staff.PostAsync(verify, null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var verified = await manager.PostAsync(verify, null);
        Assert.True(verified.StatusCode == HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        var abandoned = await verified.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Abandoned", abandoned.GetProperty("status").GetString());
        Assert.Equal("Maintenance", abandoned.GetProperty("locker").GetProperty("status").GetString());
        Assert.Equal(180_000, abandoned.GetProperty("outstandingAmount").GetDecimal());
        using var safe = await staff.PutAsJsonAsync($"/api/lockers/staff/grid/{replacementId}/maintenance",
            new { maintenance = false, safeInspection = true });
        Assert.Equal(HttpStatusCode.OK, safe.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockerRenewalUsesTransferTimeAtTheEndOfTheGracePeriod(bool transferredBeforeCutoff)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        var initial = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = initial.GetProperty("rentalId").GetInt32();
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover",
            new { termsAccepted = true, identityVerified = true, cashAmount = 250_000 });
        Assert.True(handover.IsSuccessStatusCode, await handover.Content.ReadAsStringAsync());
        var original = await handover.Content.ReadFromJsonAsync<JsonElement>();
        var originalExpiry = original.GetProperty("expiresAt").GetDateTimeOffset();
        var graceCutoff = originalExpiry.ToOffset(TimeSpan.FromHours(7)).AddDays(7);

        _clock.Now = graceCutoff.AddMinutes(-5);
        var renewal = await CreateLockerCheckout(member, $"/api/lockers/rentals/{id}/renew", new { policyId });
        var amount = renewal.GetProperty("amountDue").GetDecimal();
        Assert.Equal(320_000, amount);
        var code = renewal.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var number = renewal.GetProperty("orderNumber").GetString();
        var transferredAt = transferredBeforeCutoff ? graceCutoff.AddMinutes(-2) : graceCutoff;

        _clock.Now = graceCutoff.AddMinutes(1);
        using var callback = await NotifyTransferAsync(code, amount, 923, transferredAt);
        Assert.True(callback.IsSuccessStatusCode, await callback.Content.ReadAsStringAsync());

        var order = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{number}");
        Assert.Equal(transferredBeforeCutoff ? "Paid" : "RefundPending", order.GetProperty("status").GetString());
        var rental = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        var expectedExpiry = transferredBeforeCutoff ? graceCutoff.AddDays(-1).AddMonths(1) : originalExpiry;
        Assert.Equal(expectedExpiry, rental.GetProperty("expiresAt").GetDateTimeOffset());
        var refunds = await member.GetFromJsonAsync<JsonElement>("/api/checkout-refunds/mine");
        if (transferredBeforeCutoff)
            Assert.Empty(refunds.EnumerateArray());
        else
            Assert.Equal(amount, Assert.Single(refunds.EnumerateArray()).GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerReturnFreezesFeesAndOnlyOneSettlementIsAccepted(bool olderTransferArrivesFirst)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        var initial = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = initial.GetProperty("rentalId").GetInt32();
        var path = $"/api/lockers/staff/rentals/{id}";
        var initialNumber = initial.GetProperty("orderNumber").GetString();
        using var cash = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{initialNumber}/cash", new { amount = 250_000 });
        Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
        using var handover = await staff.PostAsJsonAsync($"{path}/handover", LockerHandover);
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
        var rental = await handover.Content.ReadFromJsonAsync<JsonElement>();
        _clock.Now = rental.GetProperty("expiresAt").GetDateTimeOffset().AddDays(1).AddMinutes(-5);

        var checkoutRequest = new { paymentMethod = "VietQR" };
        using var premature = await staff.PostAsJsonAsync($"{path}/settlement", checkoutRequest);
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        using var returned = await staff.PostAsJsonAsync($"{path}/return", new { lockerEmpty = true });
        Assert.True(returned.IsSuccessStatusCode, await returned.Content.ReadAsStringAsync());
        var quote = await returned.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10_000, quote.GetProperty("overdueFees").GetDecimal());
        var older = await CreateLockerCheckout(staff, $"{path}/settlement", checkoutRequest);
        var olderPaidAt = _clock.Now.AddMinutes(2);
        _clock.Now = _clock.Now.AddMinutes(21);
        using var repeatedReturn = await staff.PostAsJsonAsync($"{path}/return", new { lockerEmpty = true });
        Assert.Equal(HttpStatusCode.OK, repeatedReturn.StatusCode);
        var repeatedQuote = await repeatedReturn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(quote.GetProperty("returnedAt").GetDateTimeOffset(), repeatedQuote.GetProperty("returnedAt").GetDateTimeOffset());
        var newer = await CreateLockerCheckout(staff, $"{path}/settlement", checkoutRequest);
        Assert.Equal(10_000, newer.GetProperty("amountDue").GetDecimal());
        var olderCode = older.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var newerCode = newer.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var newerPaidAt = _clock.Now.AddMinutes(1);
        _clock.Now = _clock.Now.AddMinutes(2);
        var firstCode = olderTransferArrivesFirst ? olderCode : newerCode;
        var secondCode = olderTransferArrivesFirst ? newerCode : olderCode;
        var firstPaidAt = olderTransferArrivesFirst ? olderPaidAt : newerPaidAt;
        var secondPaidAt = olderTransferArrivesFirst ? newerPaidAt : olderPaidAt;
        using var first = await NotifyTransferAsync(firstCode, 10_000, 921, firstPaidAt);
        using var second = await NotifyTransferAsync(secondCode, 10_000, 922, secondPaidAt);
        using var replay = await NotifyTransferAsync(secondCode, 10_000, 922, secondPaidAt);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var refunds = await member.GetFromJsonAsync<JsonElement>("/api/checkout-refunds/mine");
        Assert.Equal(10_000, Assert.Single(refunds.EnumerateArray()).GetProperty("amount").GetDecimal());
        var winning = olderTransferArrivesFirst ? older : newer;
        using var repeatedSettlement = await staff.PostAsJsonAsync($"{path}/settlement", checkoutRequest);
        Assert.Equal(HttpStatusCode.OK, repeatedSettlement.StatusCode);
        var settled = await repeatedSettlement.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(winning.GetProperty("orderNumber").GetString(), settled.GetProperty("orderNumber").GetString());
        Assert.Equal("Paid", settled.GetProperty("status").GetString());
        using var terminated = await staff.PostAsJsonAsync($"{path}/terminate",
            new { lockerEmpty = true, lockSafe = true, settlementOrderNumber = winning.GetProperty("orderNumber").GetString() });
        Assert.True(terminated.IsSuccessStatusCode, await terminated.Content.ReadAsStringAsync());
    }

    private async Task<(int LockerId, int PolicyId)> PrepareLocker()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
        gym.OpenTime = new(6, 0);
        gym.CloseTime = new(22, 0);
        var component = await db.PackageComponents.SingleAsync(c => c.PackageId == _basic);
        db.Orders.Add(new Order
        {
            OrderNumber = "LOCKER-TEST-GYM",
            PurchaserUserId = "member",
            OrderStatus = OrderStatus.Paid,
            Items = [new OrderItem
            {
                PackageId = _basic, BeneficiaryUserId = "member",
                MemberMemberships = [new MemberMembership
                {
                    MemberUserId = "member", PackageComponentId = component.Id,
                    MembershipStatus = MembershipStatus.Active, StartDate = Today, EndDate = Today.AddDays(200)
                }]
            }]
        });
        var policy = new LockerPricingPolicy
        {
            PolicyName = "1 tháng",
            RentalType = RentalType.Monthly,
            RentalPrice = 250_000,
            OverdueDailyFee = 10_000,
            KeyReplacementFee = 100_000
        };
        db.LockerPricingPolicies.Add(policy);
        var locker = new Locker
        {
            LockerNumber = "A01",
            LockerZone = new LockerZone { Gym = gym, ZoneName = "A", FloorNumber = 1 }
        };
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        return (locker.Id, policy.Id);
    }
}
