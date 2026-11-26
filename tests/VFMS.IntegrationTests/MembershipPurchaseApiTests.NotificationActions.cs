using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Dashboards;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string NotificationsRoute = "/api/dashboards/notifications";

    [Fact]
    public async Task NotificationReadIsOwnerScopedAndIdempotent()
    {
        var ids = await SeedActionNotificationsAsync(2);
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsync($"{NotificationsRoute}/{ids[0]}/read", null)).StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await member.PostAsync($"{NotificationsRoute}/{ids[0]}/read", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<DashboardInbox>())!.Unread);
        }
        var inbox = (await member.GetFromJsonAsync<DashboardInbox>(NotificationsRoute))!;
        Assert.Equal(1, inbox.Unread);
        await using var scope = _app.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Notifications.OrderBy(n => n.Id).ToListAsync();
        Assert.True(rows.Single(n => n.Id == ids[0]).IsRead);
        Assert.False(rows.Single(n => n.Id == ids[1]).IsRead);
        Assert.False(rows.Single(n => n.RecipientUserId == "staff").IsRead);
    }

    [Fact]
    public async Task NotificationMarkAllIncludesMessagesOutsideTheDropdownAndPreservesOtherRecipients()
    {
        await SeedActionNotificationsAsync(13);
        using var member = Client("member", "Member");
        var before = (await member.GetFromJsonAsync<DashboardInbox>(NotificationsRoute))!;
        Assert.Equal(8, before.Recent.Count);
        Assert.Equal(13, before.Unread);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await member.PostAsync($"{NotificationsRoute}/read-all", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await response.Content.ReadFromJsonAsync<DashboardInbox>())!.Unread);
        }
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.False(await db.Notifications.AnyAsync(n => n.RecipientUserId == "member" && !n.IsRead));
        Assert.False((await db.Notifications.SingleAsync(n => n.RecipientUserId == "staff")).IsRead);
    }

    [Fact]
    public async Task NotificationHistoryPagesTenMessagesInStableOrderWithoutMarkingThemRead()
    {
        var ids = await SeedActionNotificationsAsync(23);
        using var member = Client("member", "Member");
        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var response = await member.GetAsync($"{NotificationsRoute}/history?page={page}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(page, data.GetProperty("page").GetInt32());
            Assert.Equal(10, data.GetProperty("pageSize").GetInt32());
            Assert.Equal(23, data.GetProperty("totalCount").GetInt32());
            var items = data.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(page < 3 ? 10 : 3, items.Length);
            Assert.All(items, item => Assert.StartsWith("Own ", item.GetProperty("title").GetString()));
            seen.AddRange(items.Select(item => item.GetProperty("id").GetInt32()));
        }
        Assert.Equal(ids.Reverse(), seen);
        Assert.Equal(23, (await member.GetFromJsonAsync<DashboardInbox>(NotificationsRoute))!.Unread);
        var empty = await member.GetFromJsonAsync<JsonElement>($"{NotificationsRoute}/history?page=4");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync($"{NotificationsRoute}/history?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync($"{NotificationsRoute}/history?page={int.MaxValue}")).StatusCode);
    }

    [Theory]
    [InlineData("member", "Member")]
    [InlineData("buyer", "Customer")]
    [InlineData("staff", "Staff")]
    [InlineData("manager", "Manager")]
    [InlineData("buyer", "PT")]
    [InlineData("buyer", "YogaInstructor")]
    [InlineData("buyer", "Admin")]
    public async Task NotificationActionsSupportEveryExistingInboxRole(string user, string role)
    {
        using var client = Client(user, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{NotificationsRoute}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{NotificationsRoute}/read-all", null)).StatusCode);
    }

    [Fact]
    public async Task NotificationHistoryAndUnreadPreviewApplyTheSameNinetyDayWindow()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.Notifications.AddRange(
                new Notification { RecipientUserId = "member", Title = "Boundary", Message = "Visible", NotifiedAt = _clock.Now.UtcDateTime.AddDays(-90) },
                new Notification { RecipientUserId = "member", Title = "Expired", Message = "Hidden", NotifiedAt = _clock.Now.UtcDateTime.AddDays(-90).AddTicks(-1) });
            await db.SaveChangesAsync();
        }
        using var member = Client("member", "Member");
        var response = await member.GetAsync($"{NotificationsRoute}/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.Equal("Boundary", Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
        var inbox = (await member.GetFromJsonAsync<DashboardInbox>(NotificationsRoute))!;
        Assert.Equal(1, inbox.Unread);
        Assert.Equal("Boundary", Assert.Single(inbox.Recent).Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotificationActionsRejectInactiveOrDeletedRecipients(bool deleted)
    {
        var ids = await SeedActionNotificationsAsync(1);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            if (deleted) user.IsDeleted = true;
            else user.AccountStatus = AccountStatus.Locked;
            await db.SaveChangesAsync();
        }
        using var client = Client("member", "Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{NotificationsRoute}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{NotificationsRoute}/{ids[0]}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{NotificationsRoute}/read-all", null)).StatusCode);
    }

    [Fact]
    public async Task NotificationActionsRequireAuthentication()
    {
        using var client = Client(null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{NotificationsRoute}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"{NotificationsRoute}/1/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"{NotificationsRoute}/read-all", null)).StatusCode);
    }

    private async Task<int[]> SeedActionNotificationsAsync(int count)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var own = Enumerable.Range(1, count).Select(i => new Notification
        {
            RecipientUserId = "member", Title = $"Own {i}", Message = "Member message",
            NotifiedAt = _clock.Now.UtcDateTime
        }).ToArray();
        db.Notifications.AddRange(own);
        db.Notifications.Add(new Notification { RecipientUserId = "staff", Title = "Staff secret", Message = "Private", NotifiedAt = _clock.Now.UtcDateTime });
        await db.SaveChangesAsync();
        return own.Select(n => n.Id).ToArray();
    }
}
