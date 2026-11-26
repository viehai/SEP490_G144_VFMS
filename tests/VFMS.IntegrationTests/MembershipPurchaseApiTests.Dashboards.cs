using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Dashboards;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task DashboardsReflectConfirmedPaymentWithoutCountingUnpaidOrdersOrAnotherGym()
    {
        using var manager = Client("manager", "Manager");
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        using var elsewhere = Client("other-staff", "Staff");
        var order = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        var pending = await Overview<ManagerOverview>(manager, "manager");
        Assert.Equal(0, pending.RevenueToday);
        Assert.Equal(order.OrderNumber, Assert.Single(pending.Counter).Number);
        Assert.Equal(1, (await Overview<StaffOverview>(staff, "staff")).PendingOrders);
        Assert.Empty((await Overview<StaffOverview>(elsewhere, "staff")).Orders);

        using var payment = await staff.PostAsJsonAsync($"{StaffPackageOrdersRoute}/{order.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(order.AmountDue));
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
        var paid = await Overview<ManagerOverview>(manager, "manager");
        Assert.Equal(order.AmountDue, paid.RevenueToday);
        Assert.Equal(order.AmountDue, paid.Revenue[^1].Value);
        Assert.Equal(7, paid.Revenue.Count);
        Assert.Empty(paid.Counter);
        Assert.Equal("Paid", Assert.Single(paid.Recent).Status);
        Assert.Equal(1, (await Overview<StaffOverview>(staff, "staff")).NewMembers);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/dashboards/manager")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/dashboards/manager?from=2000-01-01")).StatusCode);
    }

    [Fact]
    public async Task DashboardsAggregateMultiRoleAccountsAndKeepPersonalDataPrivate()
    {
        using var admin = Client("manager", "Admin");
        using var pt = Client("buyer", "PT");
        using var other = Client("member", "PT");
        using var guest = Client();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.Notifications.Add(new() { RecipientUserId = "buyer", Title = "Lịch cá nhân", NotifiedAt = _clock.Now.UtcDateTime });
            db.AuditLogs.Add(new() { ActorUserId = "manager", ActionType = AuditActionType.Login, AuditEntity = "ApplicationUser", OccurredAt = _clock.Now.UtcDateTime });
            var membership = TestSeed.Membership("member");
            db.PTPurchases.Add(new PTPurchase { PTUserId = "buyer", MemberUserId = "member", MemberMembership = membership,
                TotalSessions = 4, EndDate = Today.AddDays(14), StartDate = Today,
                Sessions = [new PTSession { MemberUserId = "member", PTUserId = "buyer", SessionDate = Today,
                    StartTime = new(8,0), EndTime = new(9,0), SessionStatus = PTSessionStatus.Completed }] });
            await db.SaveChangesAsync();
        }
        var system = await Overview<AdminOverview>(admin, "admin?days=30");
        Assert.Equal(7, system.Accounts);
        Assert.Equal(system.Accounts, system.Roles.Sum(r => r.Count));
        Assert.Equal(30, system.Activity.Count);
        Assert.Equal(1m, system.Activity[^1].Value);
        Assert.Equal(1, (await Overview<DashboardInbox>(pt, "notifications")).Unread);
        Assert.Empty((await Overview<DashboardInbox>(other, "notifications")).Recent);
        var trainer = await Overview<TrainerOverview>(pt, "pt");
        Assert.Equal(1, trainer.PTCount);
        Assert.Equal(1, trainer.Members);
        Assert.Equal(3, Assert.Single(trainer.Progress).Remaining);
        Assert.Empty((await Overview<TrainerOverview>(other, "pt")).Schedule);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/dashboards/pt")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pt.GetAsync("/api/dashboards/admin")).StatusCode);
    }

    [Fact]
    public async Task DashboardsBucketPaidSupplementRevenueAndYogaClassesByGymDay()
    {
        _clock.Now = new DateTimeOffset(2026, 11, 25, 17, 30, 0, TimeSpan.Zero);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            var studio = new YogaStudio { Gym = gym, StudioName = "Phòng Yoga" };
            var yoga = new YogaClass { InstructorUserId = "buyer", ClassName = "Lớp sáng", ClassStatus = YogaClassStatus.Published,
                YogaClassType = new YogaClassType { TypeName = "Yoga" } };
            db.YogaClassSchedules.AddRange(
                new() { YogaClass = yoga, YogaStudio = studio, ScheduledDate = Today, StartTime = new(8, 0), EndTime = new(9, 0) },
                new() { YogaClass = yoga, YogaStudio = studio, ScheduledDate = Today, StartTime = new(10, 0), EndTime = new(11, 0), InstructorUserId = "member" });
            db.SupplementOrders.AddRange(
                new() { CustomerUserId = "buyer", OrderNumber = "YESTERDAY", OrderStatus = SupplementOrderStatus.Completed,
                    CompletedAt = _clock.Now.UtcDateTime.AddMinutes(-31), TotalAmount = 12500 },
                new() { CustomerUserId = "buyer", OrderNumber = "TODAY", OrderStatus = SupplementOrderStatus.Completed,
                    CompletedAt = _clock.Now.UtcDateTime.AddMinutes(-30), TotalAmount = 25900 },
                new() { CustomerUserId = "other-staff", OrderNumber = "OTHER", OrderStatus = SupplementOrderStatus.Completed,
                    CompletedAt = _clock.Now.UtcDateTime, TotalAmount = 99999 });
            await db.SaveChangesAsync();

            var queries = scope.ServiceProvider.GetRequiredService<VFMS.Infrastructure.Repositories.DashboardQueries>();
            var teacher = await queries.TrainerAsync("buyer", true, _clock.Now.UtcDateTime, default);
            Assert.Equal(1, teacher.YogaCount);
            Assert.Equal(new TimeOnly(8, 0), Assert.Single(teacher.Schedule).Start);
            Assert.Empty((await queries.TrainerAsync("buyer", false, _clock.Now.UtcDateTime, default)).Schedule);
        }
        using var manager = Client("manager", "Manager");
        var data = await Overview<ManagerOverview>(manager, "manager?from=2026-11-25&to=2026-11-26");
        Assert.Equal(12500, data.RevenueYesterday);
        Assert.Equal(25900, data.RevenueToday);
        Assert.Equal(new decimal[] { 12500, 25900 }, data.Revenue.Select(p => p.Value));
    }

    private static async Task<T> Overview<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync("/api/dashboards/" + path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [SqlServerMigrationFact]
    public async Task DashboardsTranslateDailyRevenueAndRoleGroupsOnSqlServer()
    {
        await using var database = await LegacySchemaMigrationTests.DisposableMigrationDatabase.CreateAsync();
        await _app.DisposeAsync();
        await StartApplicationAsync(database.ConnectionString);
        await DashboardsReflectConfirmedPaymentWithoutCountingUnpaidOrdersOrAnotherGym();
        using var admin = Client("manager", "Admin");
        var overview = await Overview<AdminOverview>(admin, "admin");
        Assert.Equal(overview.Accounts, overview.Roles.Sum(group => group.Count));
    }
}
