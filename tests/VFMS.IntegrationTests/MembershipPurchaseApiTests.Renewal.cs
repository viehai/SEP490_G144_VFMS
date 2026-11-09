using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string RenewalsRoute = "/api/membership-renewals";

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("buyer", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    public async Task RenewalRequiresMemberRole(string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var list = await client.GetAsync(RenewalsRoute);
        using var create = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(1));
        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, create.StatusCode);
    }

    [Fact]
    public async Task RenewalListAndCheckoutAreRestrictedToTheMembersOwnEntitlements()
    {
        var owned = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        var other = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active, "buyer", "Customer");
        using var client = Client("member", "Member");

        var option = Assert.Single(await ListRenewalsAsync(client));
        Assert.Equal(owned.Id, option.MembershipId);
        Assert.True(option.Eligible);
        foreach (var id in new[] { other.Id, int.MaxValue })
        {
            using var response = await client.PostAsJsonAsync(RenewalsRoute, new
            {
                membershipId = id,
                purchaserUserId = "buyer"
            });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var invalid = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Orders.CountAsync());
    }

    [Theory]
    [InlineData("Frozen")]
    [InlineData("Suspended")]
    public async Task FrozenOrSuspendedMembershipCannotBeRenewed(string status)
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), Enum.Parse<MembershipStatus>(status));
        using var client = Client("member", "Member");
        var option = Assert.Single(await ListRenewalsAsync(client));
        Assert.False(option.Eligible);
        Assert.Equal(status, option.Status);
        Assert.Equal("MSG-26", option.MessageCode);

        using var response = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id));
        Assert.False(response.IsSuccessStatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MSG-26", error.GetProperty("code").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Orders.CountAsync());
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(-7, true)]
    [InlineData(-8, false)]
    public async Task RenewalUsesInclusiveGymConfiguredAdvanceAndGraceWindows(int daysToExpiry, bool expected)
    {
        var expiry = Today.AddDays(daysToExpiry);
        var original = await SeedMembershipAsync(expiry,
            daysToExpiry < 0 ? MembershipStatus.Expired : MembershipStatus.Active);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(row => row.GymName == "VFMS");
            gym.RenewalAdvanceDays = 3;
            gym.RenewalGraceDays = 7;
            await db.SaveChangesAsync();
        }

        using var client = Client("member", "Member");
        var option = Assert.Single(await ListRenewalsAsync(client));
        Assert.Equal(original.Id, option.MembershipId);
        Assert.Equal(expiry, option.EndDate);
        Assert.Equal(expected, option.Eligible);
        using var response = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id));
        if (!expected)
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.False(string.IsNullOrWhiteSpace(option.Message));
            return;
        }

        var start = expiry.AddDays(1);
        Assert.Equal(start, option.RenewalStartDate);
        Assert.Equal(500_000m, option.Price);
        var renewal = await ReadAsync(response, HttpStatusCode.Created);
        Assert.Equal(start, Assert.Single(renewal.Items).ActivationDate);

        if (daysToExpiry < 0)
        {
            using var callback = await NotifyAsync(renewal, 102);
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var renewed = await db.MemberMemberships.SingleAsync(row => row.Id != original.Id);
            Assert.Equal(start, renewed.StartDate);
            Assert.Equal(MembershipStatus.Active, renewed.MembershipStatus);
        }
    }

    [Fact]
    public async Task RenewalUsesServerSnapshotsAndFulfillsExactlyOnceAfterVerifiedPayment()
    {
        var expiry = Today.AddDays(6);
        var original = await SeedMembershipAsync(expiry, MembershipStatus.Active);
        await UpdateRenewalPlanAsync("Basic current", 625_000m, duration: 37);

        using var client = Client("member", "Member");
        var option = Assert.Single(await ListRenewalsAsync(client));
        Assert.Equal("Basic current", option.PlanName);
        Assert.Equal(625_000m, option.Price);
        Assert.Equal(expiry.AddDays(1), option.RenewalStartDate);

        var renewal = await ReadAsync(await client.PostAsJsonAsync(RenewalsRoute, new
        {
            membershipId = original.Id,
            purchaserUserId = "buyer",
            beneficiaryUserId = "gift",
            price = 1,
            activationDate = Today,
            paymentStatus = "Paid",
            paymentMethod = "Cash"
        }), HttpStatusCode.Created);
        Assert.Equal("Pending", renewal.OrderStatus);
        Assert.Equal("Pending", renewal.PaymentStatus);
        Assert.Equal("VietQR", renewal.PaymentMethod);
        Assert.Equal(TimeSpan.FromMinutes(15), renewal.ExpiresAt - renewal.CreatedAt);
        Assert.Equal(625_000m, renewal.AmountDue);
        Assert.NotNull(renewal.VietQr);

        var duplicate = await ReadAsync(await client.PostAsJsonAsync(
            RenewalsRoute, new RenewMembershipRequest(original.Id)), HttpStatusCode.OK);
        Assert.Equal(renewal.OrderNumber, duplicate.OrderNumber);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(2, await db.Orders.CountAsync());
            var order = await db.Orders.Include(row => row.Items)
                .SingleAsync(row => row.OrderNumber == renewal.OrderNumber);
            var item = Assert.Single(order.Items);
            Assert.Equal(OrderStatus.Pending, order.OrderStatus);
            Assert.Equal("member", order.PurchaserUserId);
            Assert.Equal("member", item.BeneficiaryUserId);
            Assert.Equal(original.Id, item.RenewedFromMembershipId);
            Assert.Equal(_basic, item.PackageId);
            Assert.Equal("Basic current", item.PackageNameSnapshot);
            Assert.Equal(625_000m, item.UnitPriceSnapshot);
            Assert.Equal(expiry.AddDays(1), item.ActivationStartDate);
            Assert.Equal(original.Id, (await db.MemberMemberships.SingleAsync()).Id);
            Assert.Equal(1, await db.Invoices.CountAsync());
        }

        await UpdateRenewalPlanAsync("Basic after checkout", 700_000m, duration: 4);
        using var wrongAmount = await NotifyAsync(renewal, 102, amount: 1);
        Assert.Equal(HttpStatusCode.Conflict, wrongAmount.StatusCode);
        using var verified = await NotifyAsync(renewal, 102);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var replay = await NotifyAsync(renewal, 102);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var paid = await ReadAsync(await client.GetAsync($"{Route}/{renewal.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);
        Assert.Equal("Paid", paid.PaymentStatus);
        Assert.Equal("MSG-23", paid.MessageCode);
        Assert.NotNull(paid.InvoiceNumber);
        Assert.Equal("Basic current", Assert.Single(paid.Items).PlanName);
        using var alreadyPaid = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id));
        Assert.NotEqual(HttpStatusCode.Created, alreadyPaid.StatusCode);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var memberships = await db.MemberMemberships.OrderBy(row => row.Id).ToListAsync();
            Assert.Equal(2, memberships.Count);
            Assert.Equal(original.Id, memberships[0].Id);
            Assert.Equal(original.StartDate, memberships[0].StartDate);
            Assert.Equal(expiry, memberships[0].EndDate);
            Assert.Equal(MembershipStatus.Active, memberships[0].MembershipStatus);
            Assert.Equal("member", memberships[1].MemberUserId);
            Assert.Equal(expiry.AddDays(1), memberships[1].StartDate);
            Assert.Equal(expiry.AddDays(37), memberships[1].EndDate);
            Assert.Equal(MembershipStatus.Scheduled, memberships[1].MembershipStatus);
            var renewalItem = await db.OrderItems.SingleAsync(row => row.RenewedFromMembershipId == original.Id);
            Assert.Equal(renewalItem.Id, memberships[1].OrderItemId);
            Assert.Equal(2, await db.Orders.CountAsync());
            Assert.Equal(2, await db.Invoices.CountAsync());
        }

        _clock.Now = _clock.Now.AddDays(7);
        await RunJobAsync((service, ct) => service.AdvanceMembershipsAsync(ct));
        await using var activatedScope = _app.Services.CreateAsyncScope();
        var activatedDb = activatedScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal([MembershipStatus.Expired, MembershipStatus.Active],
            await activatedDb.MemberMemberships.OrderBy(row => row.Id)
                .Select(row => row.MembershipStatus).ToListAsync());
    }

    [Fact]
    public async Task InactiveOriginalPlanCannotBeRenewed()
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var plan = await db.Packages.SingleAsync(row => row.Id == _basic);
            plan.PackageStatus = PackageStatus.Inactive;
            await db.SaveChangesAsync();
        }

        using var client = Client("member", "Member");
        Assert.False(Assert.Single(await ListRenewalsAsync(client)).Eligible);
        using var response = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id));
        Assert.False(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredRenewalAndReplacementCannotBothBeFulfilled(bool originalCallbackArrivesFirst)
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        using var client = Client("member", "Member");
        var request = new RenewMembershipRequest(original.Id);
        var abandoned = await ReadAsync(await client.PostAsJsonAsync(RenewalsRoute, request), HttpStatusCode.Created);
        var abandonedCreatedAt = _clock.Now.AddMinutes(-16);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            // Move the original order back, since the DbContext stamps creation time from the system clock.
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            await db.Orders.Where(row => row.OrderNumber == abandoned.OrderNumber)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, abandonedCreatedAt.UtcDateTime));
        }
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        var expired = await ReadAsync(await client.GetAsync($"{Route}/{abandoned.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Expired", expired.OrderStatus);
        Assert.Null(expired.VietQr);

        var retry = await ReadAsync(await client.PostAsJsonAsync(RenewalsRoute, request), HttpStatusCode.Created);
        Assert.NotEqual(abandoned.OrderNumber, retry.OrderNumber);
        var originalTransferTime = abandonedCreatedAt.AddMinutes(1);
        if (originalCallbackArrivesFirst)
        {
            using var originalCallback = await NotifyAsync(abandoned, 102, at: originalTransferTime);
            Assert.Equal(HttpStatusCode.OK, originalCallback.StatusCode);
            var cancelled = await ReadAsync(await client.GetAsync($"{Route}/{retry.OrderNumber}"), HttpStatusCode.OK);
            Assert.Equal("Cancelled", cancelled.OrderStatus);
            Assert.Equal("Cancelled", cancelled.PaymentStatus);
            Assert.Null(cancelled.VietQr);
            using var replacementCallback = await NotifyAsync(retry, 103);
            Assert.Equal(HttpStatusCode.Conflict, replacementCallback.StatusCode);
        }
        else
        {
            using var replacementCallback = await NotifyAsync(retry, 102);
            Assert.Equal(HttpStatusCode.OK, replacementCallback.StatusCode);
            using var originalCallback = await NotifyAsync(abandoned, 103, at: originalTransferTime);
            Assert.Equal(HttpStatusCode.Conflict, originalCallback.StatusCode);
        }

        await using var settledScope = _app.Services.CreateAsyncScope();
        var settledDb = settledScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(2, await settledDb.MemberMemberships.CountAsync());
        Assert.Equal(2, await settledDb.Invoices.CountAsync());
    }

    [Fact]
    public async Task GracePeriodRenewalRejectsTermsThatWouldAlreadyHaveEnded()
    {
        var original = await SeedMembershipAsync(Today.AddDays(-10), MembershipStatus.Expired);
        await UpdateRenewalPlanAsync("Basic current", 500_000m, duration: 4);
        using var client = Client("member", "Member");

        var option = Assert.Single(await ListRenewalsAsync(client));
        Assert.False(option.Eligible);
        Assert.Equal("RENEWAL_TERM_ELAPSED", option.MessageCode);

        using var response = await client.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id));
        Assert.False(response.IsSuccessStatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RENEWAL_TERM_ELAPSED", error.GetProperty("code").GetString());
    }

    private async Task<MemberMembership> SeedMembershipAsync(
        DateOnly expiry, MembershipStatus status, string user = "member", string role = "Member")
    {
        using var client = Client(user, role);
        var purchase = await CreateAsync(client, Purchase([new(_basic)]));
        using var callback = await NotifyAsync(purchase, user == "member" ? 100 : 101);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var membership = await db.MemberMemberships.SingleAsync(row => row.MemberUserId == user);
        membership.StartDate = expiry.AddDays(-29);
        membership.EndDate = expiry;
        membership.MembershipStatus = status;
        await db.SaveChangesAsync();
        return membership;
    }

    private async Task UpdateRenewalPlanAsync(string name, decimal price, int duration)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var plan = await db.Packages.Include(row => row.Components).SingleAsync(row => row.Id == _basic);
        plan.Name = name;
        plan.Price = price;
        plan.Components.Single().DurationDays = duration;
        await db.SaveChangesAsync();
    }

    private static async Task<MembershipRenewalOption[]> ListRenewalsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MembershipRenewalOption[]>(RenewalsRoute))!;
}
