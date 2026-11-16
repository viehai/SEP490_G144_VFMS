using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFMS.API.Controllers;
using VFMS.API.Extensions;
using VFMS.Application.Mappings;
using VFMS.Contracts.Auth.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed class MembershipPlanApiTests : IAsyncLifetime
{
    private const string ManagementRoute = "/api/manager/membership-plans";
    private const string CatalogRoute = "/api/membership-plans";
    private const string ManagerUserId = "membership-plan-manager";
    private const string MemberUserId = "membership-plan-member";
    private const string RoleHeader = "Test-Role";

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly SelectCounter _selectCounter = new();
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ManagerMembershipPlansController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        var dbOptions = new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlite(_connection).AddInterceptors(_selectCounter).Options;
        builder.Services.AddScoped<VfmsDbContext>(_ => new TestContext(dbOptions));
        builder.Services.AddMembershipPlanModule();
        builder.Services.AddSystemAdministrationModule();
        var mapsterConfig = new TypeAdapterConfig();
        mapsterConfig.Scan(typeof(SystemAdministrationMapping).Assembly);
        builder.Services.AddSingleton(mapsterConfig);
        builder.Services.AddScoped<IMapper, ServiceMapper>();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseDeveloperExceptionPage();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Membership plan test gym" };
        db.ApplicationUsers.AddRange(TestUser(ManagerUserId, gym), TestUser(MemberUserId, gym));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task ManagementEndpointsRequireManagerRole(string? role, HttpStatusCode expected)
    {
        using var client = CreateClient(role);
        using var list = await client.GetAsync(ManagementRoute);
        using var detail = await client.GetAsync($"{ManagementRoute}/1");
        using var create = await client.PostAsJsonAsync(ManagementRoute, PlanRequest());
        using var update = await client.PutAsJsonAsync($"{ManagementRoute}/1", PlanRequest());
        using var status = await client.PatchAsJsonAsync($"{ManagementRoute}/1/status", new { status = "INACTIVE" });
        using var delete = await client.DeleteAsync($"{ManagementRoute}/1");

        Assert.All(new[] { list, detail, create, update, status, delete }, response =>
            Assert.Equal(expected, response.StatusCode));
    }

    [Fact]
    public async Task ManagerCreatesReadsAndUpdatesPlanWithAuditHistory()
    {
        using var client = CreateClient();
        var created = await CreatePlanAsync(client, "  Monthly Basic  ", price: 0);
        var planId = created.GetProperty("id").GetInt32();
        Assert.Equal("Monthly Basic", created.GetProperty("name").GetString());
        Assert.Equal(0m, created.GetProperty("price").GetDecimal());
        Assert.Equal("ACTIVE", created.GetProperty("status").GetString());
        Assert.Equal(0, created.GetProperty("subscriberCount").GetInt32());

        var updated = await ReadBodyAsync(await client.PutAsJsonAsync($"{ManagementRoute}/{planId}", new
        {
            name = "  Annual Premium  ",
            description = "Updated description",
            durationInDays = 365,
            price = 1_250_000m,
            status = "ACTIVE"
        }), HttpStatusCode.OK);
        Assert.Equal("Annual Premium", updated.GetProperty("name").GetString());

        var detail = await ReadBodyAsync(await client.GetAsync($"{ManagementRoute}/{planId}"), HttpStatusCode.OK);
        Assert.Equal(planId, detail.GetProperty("id").GetInt32());
        Assert.Equal("Annual Premium", detail.GetProperty("name").GetString());
        Assert.Equal("Updated description", detail.GetProperty("description").GetString());
        Assert.Equal(365, detail.GetProperty("durationInDays").GetInt32());
        Assert.Equal(1_250_000m, detail.GetProperty("price").GetDecimal());
        Assert.False(detail.TryGetProperty("accessLevel", out _));
        Assert.False(detail.TryGetProperty("includedFeatures", out _));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var component = await db.PackageComponents.SingleAsync(row => row.PackageId == planId);
        Assert.Equal(ComponentType.GYM, component.ComponentType);
        Assert.Equal(365, component.DurationDays);
        var logs = await AuditsAsync(db, planId);
        Assert.Collection(logs,
            entry => AssertAudit(entry, AuditActionType.CreateMembershipPlan, oldValue: null, newValue: "Monthly Basic"),
            entry => AssertAudit(entry, AuditActionType.UpdateMembershipPlan, oldValue: "Monthly Basic", newValue: "Annual Premium"));
    }

    [Theory]
    [InlineData("name", null)]
    [InlineData("name", "   ")]
    [InlineData("durationInDays", null)]
    [InlineData("durationInDays", 0)]
    [InlineData("durationInDays", -5)]
    [InlineData("durationInDays", 3.5)]
    [InlineData("price", null)]
    [InlineData("price", -100)]
    [InlineData("status", "ARCHIVED")]
    [InlineData("status", "1")]
    [InlineData("status", null)]
    public async Task CreateRejectsInvalidInput(string field, object? value)
    {
        using var client = CreateClient();
        var request = PlanRequest();
        if (value is null)
            request.Remove(field);
        else
            request[field] = value;

        using var response = await client.PostAsJsonAsync(ManagementRoute, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.False(await db.Packages.AnyAsync());
        Assert.False(await db.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task InvalidUpdateIsRejectedAndUnknownPlanReturnsNotFound()
    {
        using var client = CreateClient();
        var plan = await CreatePlanAsync(client);
        var planId = plan.GetProperty("id").GetInt32();
        using var invalid = await client.PutAsJsonAsync($"{ManagementRoute}/{planId}", PlanRequest("  "));
        using var missingUpdate = await client.PutAsJsonAsync($"{ManagementRoute}/999", PlanRequest());
        using var missingDetail = await client.GetAsync($"{ManagementRoute}/999");
        using var missingStatus = await client.PatchAsJsonAsync($"{ManagementRoute}/999/status", new { status = "INACTIVE" });
        using var missingDelete = await client.DeleteAsync($"{ManagementRoute}/999");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.All(new[] { missingUpdate, missingDetail, missingStatus, missingDelete }, response =>
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(plan.GetProperty("name").GetString(), (await db.Packages.SingleAsync()).Name);
        Assert.Single(await AuditsAsync(db, planId));
    }

    [Fact]
    public async Task ManagerCanDeactivateAndReactivateAndBothTransitionsAreAudited()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        using var invalid = await client.PatchAsJsonAsync($"{ManagementRoute}/{planId}/status", new { status = "UNKNOWN" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var inactive = await ReadBodyAsync(await client.PatchAsJsonAsync($"{ManagementRoute}/{planId}/status",
            new { status = "INACTIVE" }), HttpStatusCode.OK);
        Assert.Equal("INACTIVE", inactive.GetProperty("status").GetString());

        var active = await ReadBodyAsync(await client.PatchAsJsonAsync($"{ManagementRoute}/{planId}/status",
            new { status = "ACTIVE" }), HttpStatusCode.OK);
        Assert.Equal("ACTIVE", active.GetProperty("status").GetString());

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(PackageStatus.Active, (await db.Packages.SingleAsync()).PackageStatus);
        var logs = await AuditsAsync(db, planId);
        Assert.Equal(3, logs.Count);
        AssertAudit(logs[1], AuditActionType.DeactivateMembershipPlan, oldValue: "ACTIVE", newValue: "INACTIVE", key: "Status");
        AssertAudit(logs[2], AuditActionType.ActivateMembershipPlan, oldValue: "INACTIVE", newValue: "ACTIVE", key: "Status");
    }

    [Fact]
    public async Task UpdateChangingStatusWritesUpdateAndStatusAudits()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        var updated = await ReadBodyAsync(await client.PutAsJsonAsync($"{ManagementRoute}/{planId}",
            PlanRequest("Renamed plan", status: "INACTIVE")), HttpStatusCode.OK);
        Assert.Equal("INACTIVE", updated.GetProperty("status").GetString());

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var actions = (await AuditsAsync(db, planId)).Select(log => log.ActionType).ToArray();
        Assert.Equal(3, actions.Length);
        Assert.Contains(AuditActionType.UpdateMembershipPlan, actions);
        Assert.Contains(AuditActionType.DeactivateMembershipPlan, actions);
    }

    [Fact]
    public async Task AdminCanFilterAndReadMembershipPlanAuditActions()
    {
        using var manager = CreateClient();
        var planId = (await CreatePlanAsync(manager)).GetProperty("id").GetInt32();
        await ReadBodyAsync(await manager.PatchAsJsonAsync($"{ManagementRoute}/{planId}/status",
            new { status = "INACTIVE" }), HttpStatusCode.OK);
        await ReadBodyAsync(await manager.PatchAsJsonAsync($"{ManagementRoute}/{planId}/status",
            new { status = "ACTIVE" }), HttpStatusCode.OK);

        using var admin = CreateClient(nameof(UserRole.Admin));
        const string auditRoute = "/api/admin/audit-logs";
        foreach (var action in new[] { AuditActionType.ActivateMembershipPlan, AuditActionType.DeactivateMembershipPlan })
        {
            var filtered = await ReadBodyAsync(await admin.GetAsync($"{auditRoute}?actionType={action}&entity=Package"), HttpStatusCode.OK);
            Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
            var item = Assert.Single(filtered.GetProperty("items").EnumerateArray());
            Assert.Equal(planId, item.GetProperty("entityId").GetInt32());
            Assert.Equal(ManagerUserId, item.GetProperty("actorUserId").GetString());
            Assert.Equal(action.ToString(), item.GetProperty("actionType").GetString());

            var auditId = item.GetProperty("id").GetInt32();
            var detail = await ReadBodyAsync(await admin.GetAsync($"{auditRoute}/{auditId}"), HttpStatusCode.OK);
            Assert.Equal(action.ToString(), detail.GetProperty("actionType").GetString());
        }
    }

    [Fact]
    public async Task UnsoldPlanAndItsComponentsArePermanentlyDeletedAndAudited()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        db.PackageComponents.Add(new PackageComponent { PackageId = planId, ComponentType = ComponentType.PT, IsDeleted = true });
        await db.SaveChangesAsync();

        var deleted = await ReadBodyAsync(await client.DeleteAsync($"{ManagementRoute}/{planId}"), HttpStatusCode.OK);
        Assert.False(deleted.GetProperty("deactivated").GetBoolean());

        Assert.False(await db.Packages.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.PackageComponents.IgnoreQueryFilters().AnyAsync());
        var logs = await AuditsAsync(db, planId);
        Assert.Equal(2, logs.Count);
        AssertAudit(logs[1], AuditActionType.DeleteMembershipPlan, oldValue: "Monthly Basic", newValue: null);
    }

    [Fact]
    public async Task PaidPlanWithoutSubscriptionsIsDeactivatedAndKeepsFinancialHistory()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        await SeedOrderAsync(planId, OrderStatus.Paid);
        await AssertProtectedDeletionAsync(client, planId);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var item = await db.OrderItems.Include(row => row.Order).ThenInclude(order => order.PaymentTransaction)
            .ThenInclude(payment => payment!.Invoice).SingleAsync();
        Assert.Equal(planId, item.PackageId);
        Assert.Equal("Monthly Basic", item.PackageNameSnapshot);
        Assert.Equal(500_000m, item.UnitPriceSnapshot);
        Assert.Equal(OrderStatus.Paid, item.Order.OrderStatus);
        Assert.Equal(PaymentStatus.Paid, item.Order.PaymentTransaction!.PaymentStatus);
        Assert.Equal(500_000m, item.Order.PaymentTransaction.AmountReceived);
        Assert.Equal(InvoiceStatus.Issued, item.Order.PaymentTransaction.Invoice!.InvoiceStatus);
        Assert.False(await db.MemberMemberships.AnyAsync());
    }

    [Fact]
    public async Task ActiveSubscriptionPreventsDeletionAndRetainsAccessToInactivePlan()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        await SeedOrderAsync(planId, OrderStatus.Pending, MembershipStatus.Active);
        await AssertProtectedDeletionAsync(client, planId);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var membership = await db.MemberMemberships.Include(row => row.PackageComponent).ThenInclude(component => component.Package)
            .Include(row => row.OrderItem).SingleAsync();
        Assert.Equal(MembershipStatus.Active, membership.MembershipStatus);
        Assert.Equal(MemberUserId, membership.MemberUserId);
        Assert.Equal(planId, membership.PackageComponent.Package.Id);
        Assert.Equal(PackageStatus.Inactive, membership.PackageComponent.Package.PackageStatus);
        Assert.Equal("Monthly Basic", membership.PackageComponent.Package.Name);
        Assert.Equal(30, membership.PackageComponent.DurationDays);
        Assert.Equal("Monthly Basic", membership.OrderItem.PackageNameSnapshot);

        var detail = await ReadBodyAsync(await client.GetAsync($"{ManagementRoute}/{planId}"), HttpStatusCode.OK);
        Assert.Equal(1, detail.GetProperty("subscriberCount").GetInt32());
    }

    [Fact]
    public async Task SoftDeletedOrderItemStillPreventsHardDeletion()
    {
        using var client = CreateClient();
        var planId = (await CreatePlanAsync(client)).GetProperty("id").GetInt32();
        await SeedOrderAsync(planId, OrderStatus.Cancelled, hideOrderItem: true);
        await AssertProtectedDeletionAsync(client, planId);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.True((await db.OrderItems.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task ListFiltersPaginatesAndCountsActiveSubscribersInTwoQueries()
    {
        using var client = CreateClient();
        var activeId = (await CreatePlanAsync(client, "Basic Monthly")).GetProperty("id").GetInt32();
        await CreatePlanAsync(client, "Basic Annual");
        await CreatePlanAsync(client, "Basic Archived", status: "INACTIVE");
        await CreatePlanAsync(client, "Premium Monthly");
        await SeedOrderAsync(activeId, OrderStatus.Paid, MembershipStatus.Active);
        await SeedOrderAsync(activeId, OrderStatus.Paid, MembershipStatus.Expired);
        await SeedOrderAsync(activeId, OrderStatus.Paid, MembershipStatus.Frozen);

        _selectCounter.Count = 0;
        var filtered = await ReadBodyAsync(await client.GetAsync(
            $"{ManagementRoute}?search=Basic&status=ACTIVE&page=1&pageSize=2"), HttpStatusCode.OK);
        Assert.Equal(2, _selectCounter.Count);
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        var items = filtered.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(1, items.Single(row => row.GetProperty("id").GetInt32() == activeId).GetProperty("subscriberCount").GetInt32());
        Assert.Equal(0, items.Single(row => row.GetProperty("id").GetInt32() != activeId).GetProperty("subscriberCount").GetInt32());

        var secondPage = await ReadBodyAsync(await client.GetAsync(
            $"{ManagementRoute}?search=Basic&status=ACTIVE&page=2&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(2, secondPage.GetProperty("totalCount").GetInt32());
        Assert.Single(secondPage.GetProperty("items").EnumerateArray());

        var inactive = await ReadBodyAsync(await client.GetAsync($"{ManagementRoute}?status=INACTIVE"), HttpStatusCode.OK);
        Assert.Equal("Basic Archived", Assert.Single(inactive.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task EditingLegacyComboKeepsOriginalCalendarMonthsAndOtherEntitlements()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var combo = new Package
        {
            Name = "Legacy combo",
            Price = 1_000_000m,
            Components =
            [
                new() { ComponentType = ComponentType.GYM, DurationMonths = 2, AllowFreeze = true },
                new() { ComponentType = ComponentType.PT, DurationDays = 90, SessionCount = 6 }
            ]
        };
        db.Packages.Add(combo);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await SeedOrderAsync(combo.Id, OrderStatus.Paid, MembershipStatus.Active, componentType: ComponentType.PT);

        using var manager = CreateClient();
        var url = $"{ManagementRoute}/{combo.Id}";
        var original = await ReadBodyAsync(await manager.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal(2, original.GetProperty("durationInMonths").GetInt32());
        Assert.Equal(0, original.GetProperty("subscriberCount").GetInt32());
        var displayedDuration = original.GetProperty("durationInDays").GetInt32();
        Assert.Equal(60, displayedDuration);
        var request = PlanRequest("Legacy combo renamed");
        request["durationInDays"] = displayedDuration;
        var updated = await ReadBodyAsync(await manager.PutAsJsonAsync(url, request), HttpStatusCode.OK);
        Assert.Equal(2, updated.GetProperty("durationInMonths").GetInt32());

        var components = await db.PackageComponents.Where(row => row.PackageId == combo.Id).ToListAsync();
        var gym = components.Single(row => row.ComponentType == ComponentType.GYM);
        Assert.Null(gym.DurationDays);
        Assert.Equal(2, gym.DurationMonths);
        Assert.True(gym.AllowFreeze);
        var trainer = components.Single(row => row.ComponentType == ComponentType.PT);
        Assert.Equal(6, trainer.SessionCount);
        Assert.Equal(90, trainer.DurationDays);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task PurchaseCatalogReturnsOnlyActivePlansAndRejectsDirectAccessToInactivePlans(string role)
    {
        using var manager = CreateClient();
        var activeId = (await CreatePlanAsync(manager, "Available Plan")).GetProperty("id").GetInt32();
        var inactiveId = (await CreatePlanAsync(manager, "Retired Plan", status: "INACTIVE")).GetProperty("id").GetInt32();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var trainerOnly = new Package
        {
            Name = "Trainer-only plan",
            Components = [new() { ComponentType = ComponentType.PT, SessionCount = 5 }]
        };
        db.Packages.Add(trainerOnly);
        await db.SaveChangesAsync();

        using var buyer = CreateClient(role);

        var catalog = await ReadBodyAsync(await buyer.GetAsync(CatalogRoute), HttpStatusCode.OK);
        var available = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        Assert.Equal(activeId, available.GetProperty("id").GetInt32());
        Assert.False(available.TryGetProperty("subscriberCount", out _));
        Assert.False(available.TryGetProperty("accessLevel", out _));
        Assert.False(available.TryGetProperty("includedFeatures", out _));
        var detail = await ReadBodyAsync(await buyer.GetAsync($"{CatalogRoute}/{activeId}"), HttpStatusCode.OK);
        Assert.Equal(activeId, detail.GetProperty("id").GetInt32());
        using var unavailable = await buyer.GetAsync($"{CatalogRoute}/{inactiveId}");
        using var trainerPlan = await buyer.GetAsync($"{CatalogRoute}/{trainerOnly.Id}");
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, trainerPlan.StatusCode);
    }

    [Fact]
    public async Task FailedAuditWriteRollsBackNewPackageAndGymComponent()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_membership_audit BEFORE INSERT ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'test audit failure');
            END;
            """);

        using var client = CreateClient();
        using var failed = await client.PostAsJsonAsync(ManagementRoute, PlanRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(await db.Packages.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.PackageComponents.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.AuditLogs.AnyAsync());
    }

    private HttpClient CreateClient(string? role = "Manager")
    {
        var client = _app.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    private static ApplicationUser TestUser(string id, Gym gym) => new()
    {
        UserId = id,
        Gym = gym,
        FullName = id,
        Email = $"{id}@example.com",
        PasswordHash = "unused-test-password-hash",
        AccountStatus = AccountStatus.Active
    };

    private static Dictionary<string, object?> PlanRequest(string name = "Monthly Basic", decimal price = 500_000m,
        string status = "ACTIVE") => new()
    {
        ["name"] = name,
        ["description"] = "A monthly gym membership",
        ["durationInDays"] = 30,
        ["price"] = price,
        ["status"] = status
    };

    private static async Task<JsonElement> CreatePlanAsync(HttpClient client, string name = "Monthly Basic",
        decimal price = 500_000m, string status = "ACTIVE") =>
        await ReadBodyAsync(await client.PostAsJsonAsync(ManagementRoute, PlanRequest(name, price, status)), HttpStatusCode.Created);

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. {body}");
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
    }

    private async Task SeedOrderAsync(int planId, OrderStatus status, MembershipStatus? membershipStatus = null,
        bool hideOrderItem = false, ComponentType componentType = ComponentType.GYM)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var package = await db.Packages.Include(row => row.Components).SingleAsync(row => row.Id == planId);
        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}",
            PurchaserUserId = MemberUserId,
            Channel = OrderChannel.Online,
            OrderStatus = status
        };
        var item = new OrderItem
        {
            Order = order,
            Package = package,
            BeneficiaryUserId = MemberUserId,
            UnitPriceSnapshot = package.Price,
            PackageNameSnapshot = package.Name,
            IsDeleted = hideOrderItem
        };
        db.OrderItems.Add(item);

        if (status == OrderStatus.Paid)
        {
            order.CompletedAt = DateTime.UtcNow;
            db.Invoices.Add(new Invoice
            {
                InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                IssuedAt = DateTime.UtcNow,
                PaymentTransaction = new PaymentTransaction
                {
                    Order = order,
                    UserId = MemberUserId,
                    GatewayName = "Test",
                    PaymentMethod = PaymentMethod.VietQR,
                    PaymentStatus = PaymentStatus.Paid,
                    AmountDue = package.Price,
                    AmountReceived = package.Price,
                    PaidAt = DateTime.UtcNow
                }
            });
        }

        if (membershipStatus.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.MemberMemberships.Add(new MemberMembership
            {
                OrderItem = item,
                PackageComponent = package.Components.Single(component => component.ComponentType == componentType),
                MemberUserId = MemberUserId,
                MembershipStatus = membershipStatus.Value,
                StartDate = today.AddDays(-5),
                EndDate = membershipStatus == MembershipStatus.Expired ? today.AddDays(-1) : today.AddDays(25)
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task AssertProtectedDeletionAsync(HttpClient client, int planId)
    {
        var deleted = await ReadBodyAsync(await client.DeleteAsync($"{ManagementRoute}/{planId}"), HttpStatusCode.OK);
        Assert.True(deleted.GetProperty("deactivated").GetBoolean());
        var message = deleted.GetProperty("message").GetString();
        Assert.Contains("permanently deleted", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inactive", message, StringComparison.OrdinalIgnoreCase);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var persisted = await db.Packages.SingleAsync(row => row.Id == planId);
        Assert.Equal(PackageStatus.Inactive, persisted.PackageStatus);
        var logs = await AuditsAsync(db, planId);
        Assert.Equal(2, logs.Count);
        AssertAudit(logs[1], AuditActionType.DeactivateMembershipPlan, oldValue: "ACTIVE", newValue: "INACTIVE", key: "Status");
    }

    private static Task<List<AuditLog>> AuditsAsync(VfmsDbContext db, int planId) =>
        db.AuditLogs.Where(log => log.AuditEntity == nameof(Package) && log.EntityId == planId)
            .OrderBy(log => log.Id).ToListAsync();

    private static void AssertAudit(AuditLog entry, AuditActionType action, string? oldValue, string? newValue, string key = "Name")
    {
        Assert.Equal(ManagerUserId, entry.ActorUserId);
        Assert.Equal(action, entry.ActionType);
        Assert.InRange(entry.OccurredAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(oldValue, ReadSnapshotValue(entry.OldValue, key));
        Assert.Equal(newValue, ReadSnapshotValue(entry.NewValue, key));
    }

    private static string? ReadSnapshotValue(string? snapshot, string key) =>
        snapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(snapshot).GetProperty(key).GetString();

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var roleName = role.ToString();
            var userId = roleName == "Manager" ? ManagerUserId : MemberUserId;
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Role, roleName)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
            modelBuilder.Entity<Voucher>().Property(voucher => voucher.Code).UseCollation("NOCASE");
        }
    }

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
