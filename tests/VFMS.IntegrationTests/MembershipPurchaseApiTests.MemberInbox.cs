using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Dashboards;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberInboxRejectsLockedOrDeletedRecipients(bool deleted)
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            if (deleted) user.IsDeleted = true;
            else user.AccountStatus = AccountStatus.Locked;
            await db.SaveChangesAsync();
        }
        using var client = Client("member", "Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboards/notifications")).StatusCode);
    }

    [Theory]
    [InlineData("member", "Member")]
    [InlineData("buyer", "Customer")]
    public async Task MemberInboxShowsOnlyTheAuthenticatedRecipientsMessages(string id, string role)
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.Notifications.AddRange(
                new Notification { RecipientUserId = id, Title = "Your payment", Message = "Own message", NotifiedAt = _clock.Now.UtcDateTime },
                new Notification { RecipientUserId = "staff", Title = "Staff secret", Message = "Other message", NotifiedAt = _clock.Now.UtcDateTime });
            await db.SaveChangesAsync();
        }
        using var client = Client(id, role);
        using var response = await client.GetAsync("/api/dashboards/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inbox = (await response.Content.ReadFromJsonAsync<DashboardInbox>())!;
        Assert.Equal("Your payment", Assert.Single(inbox.Recent).Title);
        Assert.Equal(1, inbox.Unread);
        using var guest = _app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/dashboards/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboards/admin")).StatusCode);
    }
}
