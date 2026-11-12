using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Lockers;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private static readonly object LockerHandover = new { termsAccepted = true, identityVerified = true };

    [Theory]
    [InlineData("no-worker", true)]
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

        _clock.Now = deadline.AddMinutes(2);
        if (situation is "expired" or "reallocated")
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LockerService>().Expire(CancellationToken.None);
        }
        string? replacement = null;
        if (situation == "reallocated")
        {
            var next = await CreateLockerCheckout(member, "/api/lockers/rentals", request);
            replacement = next.GetProperty("orderNumber").GetString();
        }

        var code = checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!;
        var amount = checkout.GetProperty("amountDue").GetDecimal();
        using var callback = await NotifyTransferAsync(code, amount, 913, transferAt);
        Assert.True(callback.StatusCode == HttpStatusCode.OK, await callback.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(code, amount, 913, transferAt);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var order = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{number}");
        Assert.Equal(fulfilled ? "Paid" : "ReconciliationRequired", order.GetProperty("status").GetString());
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
        Assert.Equal(1, await db.LockerRentalPayments.CountAsync(p => p.Purpose == LockerPaymentPurpose.UnappliedTransfer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerExpiredPaymentOrPickupReleasesTheLockerAndTracksRefund(bool latePayment)
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
            _clock.Now = transfer.AddHours(24);
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LockerService>().Expire(CancellationToken.None);
        }
        var grid = await member.GetFromJsonAsync<JsonElement>("/api/lockers");
        Assert.Equal("Available", Assert.Single(grid.EnumerateArray()).GetProperty("status").GetString());
        var refunds = await staff.GetFromJsonAsync<JsonElement>("/api/lockers/staff/refunds");
        var pending = Assert.Single(refunds.EnumerateArray());
        Assert.Equal(amount, pending.GetProperty("refundAmount").GetDecimal());
        var refundPath = $"/api/lockers/staff/orders/{orderNumber}/refund";
        using var completed = await staff.PostAsJsonAsync(refundPath, new { method = "BankTransfer", reference = "TRANSFER-REFUND" });
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var result = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Refunded", result.GetProperty("status").GetString());
        using var replay = await NotifyTransferAsync(transferContent, amount, 911, transfer);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var rest = await staff.GetFromJsonAsync<JsonElement>("/api/lockers/staff/refunds");
        Assert.Empty(rest.EnumerateArray());
    }

    [Fact]
    public async Task LockerDailyCashReturnsDepositAndReleasesLockerExactlyOnce()
    {
        var (lockerId, _) = await PrepareLocker();
        _clock.Now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(7));
        using var manager = Client("manager", "Manager");
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        using var policy = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            dailyFee = 20_000, dailyDeposit = 100_000, oneMonth = 200_000, threeMonths = 550_000,
            sixMonths = 1_000_000, twelveMonths = 1_900_000, overdueDailyFee = 10_000, keyReplacementFee = 100_000
        });
        Assert.True(policy.StatusCode == HttpStatusCode.OK, await policy.Content.ReadAsStringAsync());
        var tiers = await policy.Content.ReadFromJsonAsync<JsonElement>();
        var policyId = Assert.Single(tiers.EnumerateArray(), t => t.GetProperty("type").GetString() == "Daily").GetProperty("id").GetInt32();
        using var online = await member.PostAsJsonAsync("/api/lockers/rentals", new { lockerId, policyId });
        Assert.Equal(HttpStatusCode.BadRequest, online.StatusCode);
        var checkout = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var orderNumber = checkout.GetProperty("orderNumber").GetString();
        var rentalId = checkout.GetProperty("rentalId").GetInt32();
        Assert.Equal(120_000, checkout.GetProperty("amountDue").GetDecimal());
        using var paid = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{orderNumber}/cash", new { amount = 120_000 });
        Assert.True(paid.StatusCode == HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        var rentalPath = $"/api/lockers/staff/rentals/{rentalId}";
        using var handover = await staff.PostAsJsonAsync($"{rentalPath}/handover", LockerHandover);
        Assert.True(handover.StatusCode == HttpStatusCode.OK, await handover.Content.ReadAsStringAsync());
        var rental = await handover.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_clock.Now.UtcDateTime.Date.AddHours(15), rental.GetProperty("expiresAt").GetDateTime());
        Assert.Equal(100_000, rental.GetProperty("depositHeld").GetDecimal());
        var close = new { lockerEmpty = true, lockSafe = true, refundMethod = "Cash" };
        using var terminate = await staff.PostAsJsonAsync($"{rentalPath}/terminate", close);
        Assert.True(terminate.StatusCode == HttpStatusCode.OK, await terminate.Content.ReadAsStringAsync());
        var closed = await terminate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Terminated", closed.GetProperty("status").GetString());
        Assert.Equal(100_000, closed.GetProperty("refundedDeposit").GetDecimal());
        using var retry = await staff.PostAsJsonAsync($"{rentalPath}/terminate", close);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(1, await db.LockerRentalPayments.CountAsync());
        Assert.Equal(LockerStatus.Available, (await db.Lockers.SingleAsync()).LockerStatus);
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
        Assert.Equal(_clock.Now.UtcDateTime.AddMonths(1), originalExpiry);

        _clock.Now = new DateTimeOffset(originalExpiry).AddDays(-1);
        var renewal = await CreateLockerCheckout(staff, $"{staffPath}/renew", new { policyId, paymentMethod = "Cash" });
        var renewalNumber = renewal.GetProperty("orderNumber").GetString();
        using var renewed = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{renewalNumber}/cash", new { amount = 250_000 });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal(lockerId, mine.GetProperty("locker").GetProperty("id").GetInt32());
        var renewedExpiry = mine.GetProperty("expiresAt").GetDateTime();
        Assert.Equal(originalExpiry.AddMonths(1), renewedExpiry);
        _clock.Now = new DateTimeOffset(renewedExpiry).AddDays(7);
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
        using var safe = await staff.PutAsJsonAsync($"/api/lockers/staff/grid/{lockerId}/maintenance",
            new { maintenance = false, safeInspection = true });
        Assert.Equal(HttpStatusCode.OK, safe.StatusCode);
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
            OrderNumber = "LOCKER-TEST-GYM", PurchaserUserId = "member", OrderStatus = OrderStatus.Paid,
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
            PolicyName = "1 tháng", RentalType = RentalType.Monthly, RentalPrice = 250_000,
            OverdueDailyFee = 10_000, KeyReplacementFee = 100_000
        };
        db.LockerPricingPolicies.Add(policy);
        var locker = new Locker
        {
            LockerNumber = "A01", LockerZone = new LockerZone { Gym = gym, ZoneName = "A", FloorNumber = 1 }
        };
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        return (locker.Id, policy.Id);
    }
}
