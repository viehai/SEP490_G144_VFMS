using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFMS.API.Controllers;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Workers;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests : IAsyncLifetime
{
    private const string Route = "/api/membership-purchases";
    private const string WebhookRoute = "/api/v1/commerce/payment/webhook/sepay";
    private const string Secret = "uc12-test-signing-secret";
    private const string BankAccount = "123456789";
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly TestClock _clock = new();
    private WebApplication _app = null!;
    private int _basic;
    private int _combo;
    private int _inactive;
    private DateOnly Today => DateOnly.FromDateTime(_clock.Now.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(MembershipPurchasesController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<MembershipPurchaseRequestValidator>();
        var dbOptions = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        builder.Services.AddScoped<VfmsDbContext>(_ => new TestContext(dbOptions));
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Configuration["MembershipPayments:BankId"] = "MB";
        builder.Configuration["MembershipPayments:AccountNumber"] = BankAccount;
        builder.Configuration["MembershipPayments:AccountName"] = "Test VFMS";
        builder.Configuration["MembershipPayments:WebhookSecret"] = Secret;
        builder.Services.AddMembershipPurchaseModule(builder.Configuration);
        builder.Services.AddMembershipFreezePolicyModule();
        builder.Services.AddMembershipFreezeRequestModule();
        var worker = builder.Services.Single(service =>
            service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(MembershipCommerceWorker));
        builder.Services.Remove(worker);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
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
        var gym = new Gym { GymName = "VFMS" };
        var otherGym = new Gym { GymName = "Other gym" };
        var roles = new[] { "Customer", "Member", "Staff", "Manager", "PT" }
            .ToDictionary(role => role, role => new ApplicationRole { RoleName = role });
        db.ApplicationUsers.AddRange(
            User("buyer", gym, roles["Customer"], roles["PT"]), User("member", gym, roles["Member"]),
            User("gift", gym, roles["Customer"]), User("locked", gym, roles["Customer"], status: AccountStatus.Locked),
            User("staff", gym, roles["Staff"]), User("other-staff", otherGym, roles["Staff"]),
            User("manager", gym, roles["Manager"]));
        var basic = Plan("Basic", 500_000m, PackageStatus.Active);
        var combo = Plan("Combo", 800_000m, PackageStatus.Active);
        combo.Components.Add(new PackageComponent { ComponentType = ComponentType.YOGA, DurationMonths = 1 });
        var inactive = Plan("Inactive", 200_000m, PackageStatus.Inactive);
        db.Packages.AddRange(basic, combo, inactive);
        await db.SaveChangesAsync();
        (_basic, _combo, _inactive) = (basic.Id, combo.Id, inactive.Id);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("locked", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "Customer", HttpStatusCode.Created)]
    [InlineData("member", "Member", HttpStatusCode.Created)]
    public async Task PurchaseRequiresAnEligibleBuyer(string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var response = await client.PostAsJsonAsync(Route, Purchase([new(_basic)]));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MultiPlanOrderUsesServerPricesAndIdentityAndDoesNotProvisionUnpaidAccess()
    {
        using var client = Client("buyer", "Customer");
        using var response = await client.PostAsJsonAsync(Route, new
        {
            purchaserUserId = "member",
            paymentStatus = "Paid",
            subtotal = 2,
            amountDue = 2,
            paymentMethod = "VietQR",
            items = new[]
            {
                new { planId = _basic, beneficiaryEmail = (string?)null, activationDate = Today, unitPrice = 1 },
                new { planId = _combo, beneficiaryEmail = (string?)"GIFT@example.test", activationDate = Today.AddDays(30), unitPrice = 1 }
            }
        });
        var purchase = await ReadAsync(response, HttpStatusCode.Created);
        Assert.Equal("Pending", purchase.OrderStatus);
        Assert.Equal("Pending", purchase.PaymentStatus);
        Assert.Equal(1_300_000m, purchase.Subtotal);
        Assert.Equal(1_300_000m, purchase.AmountDue);
        Assert.Equal(TimeSpan.FromMinutes(15), purchase.ExpiresAt - purchase.CreatedAt);
        Assert.Contains("amount=1300000", purchase.VietQr!.ImageUrl);
        Assert.Contains(purchase.VietQr.TransferContent, purchase.VietQr.ImageUrl);
        Assert.Collection(purchase.Items, line => Assert.True(line.IsForPurchaser), line => Assert.False(line.IsForPurchaser));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var order = await db.Orders.Include(row => row.Items).SingleAsync();
        Assert.Equal("buyer", order.PurchaserUserId);
        Assert.Equal(OrderStatus.Pending, order.OrderStatus);
        Assert.Equal(["buyer", "gift"], order.Items.OrderBy(item => item.Id).Select(item => item.BeneficiaryUserId));
        Assert.Equal([500_000m, 800_000m], order.Items.OrderBy(item => item.Id).Select(item => item.UnitPriceSnapshot));
        Assert.Empty(await db.MemberMemberships.ToListAsync());
        Assert.Empty(await db.Invoices.ToListAsync());
        using var member = Client("member", "Member");
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{Route}/{purchase.OrderNumber}")).StatusCode);
    }

    [Fact]
    public async Task EmptyUnknownInactiveOrIneligibleItemsRejectEntireOrder()
    {
        using var client = Client("buyer", "Customer");
        var requests = new[]
        {
            Purchase([]), Purchase([new(_basic), new(int.MaxValue)]), Purchase([new(_basic), new(_inactive)]),
            Purchase([new(_basic, "missing@example.test")]), Purchase([new(_basic, "locked@example.test")]),
            Purchase([new(_basic, "staff@example.test")])
        };
        foreach (var request in requests)
        {
            using var response = await client.PostAsJsonAsync(Route, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Orders.ToListAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task ActivationOutsideWindowReturnsMsg25(int offset)
    {
        using var client = Client("buyer", "Customer");
        using var response = await client.PostAsJsonAsync(Route, Purchase([new(_basic, ActivationDate: Today.AddDays(offset))]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MSG-25", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmedTransferProvisionsEachComponentAndIsIdempotentAfterCatalogEdits()
    {
        using var client = Client("buyer", "Customer");
        var activation = Today.AddDays(1);
        var purchase = await CreateAsync(client, Purchase([new(_basic), new(_combo, "gift@example.test", activation)]));
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var basic = await db.Packages.Include(plan => plan.Components).SingleAsync(plan => plan.Id == _basic);
            basic.Name = "New name";
            basic.Price = 10;
            basic.PackageStatus = PackageStatus.Inactive;
            basic.Components.Single().DurationDays = 5;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(purchase, id: 10)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(purchase, id: 10)).StatusCode);
        using var get = await client.GetAsync($"{Route}/{purchase.OrderNumber}");
        var paid = await ReadAsync(get, HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);
        Assert.Equal("Paid", paid.PaymentStatus);
        Assert.Equal("MSG-23", paid.MessageCode);
        Assert.NotNull(paid.InvoiceNumber);
        Assert.Equal("Basic", paid.Items[0].PlanName);
        Assert.Equal(1_300_000m, paid.AmountDue);
        Assert.Null(paid.VietQr);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var memberships = await db.MemberMemberships.OrderBy(row => row.MemberUserId).ToListAsync();
            Assert.Equal(3, memberships.Count);
            Assert.Equal(MembershipStatus.Active, memberships[0].MembershipStatus);
            Assert.Equal(Today.AddDays(29), memberships[0].EndDate);
            Assert.All(memberships.Skip(1), membership =>
            {
                Assert.Equal("gift", membership.MemberUserId);
                Assert.Equal(MembershipStatus.Scheduled, membership.MembershipStatus);
                Assert.Equal(activation, membership.StartDate);
            });
            Assert.Equal(["Member", "PT"], await RolesAsync(db, "buyer"));
            Assert.Equal(["Customer"], await RolesAsync(db, "gift"));
            Assert.Single(await db.Invoices.ToListAsync());
        }
        _clock.Now = _clock.Now.AddDays(1);
        await RunJobAsync((service, ct) => service.AdvanceMembershipsAsync(ct));
        await using var activatedScope = _app.Services.CreateAsyncScope();
        var activatedDb = activatedScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(["Member"], await RolesAsync(activatedDb, "gift"));
        Assert.All(await activatedDb.MemberMemberships.ToListAsync(), membership =>
            Assert.Equal(MembershipStatus.Active, membership.MembershipStatus));
    }

    [Fact]
    public async Task SameServiceQueuesAcrossOrdersAndDifferentServicesStartInParallel()
    {
        using var client = Client("buyer", "Customer");
        var first = await CreateAsync(client, Purchase([new(_basic)]));
        var second = await CreateAsync(client, Purchase([new(_combo), new(_basic)]));
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(first, id: 20)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(second, id: 21)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var memberships = await db.MemberMemberships.Include(row => row.PackageComponent).ToListAsync();
        Assert.Equal([Today, Today.AddDays(30), Today.AddDays(60)], memberships
            .Where(row => row.PackageComponent.ComponentType == ComponentType.GYM).Select(row => row.StartDate).Order());
        Assert.Equal(Today, Assert.Single(memberships, row => row.PackageComponent.ComponentType == ComponentType.YOGA).StartDate);
    }

    [Fact]
    public async Task WebhookRejectsTamperingWrongAmountAndReusedGatewayId()
    {
        using var client = Client("buyer", "Customer");
        var first = await CreateAsync(client, Purchase([new(_basic)]));
        var second = await CreateAsync(client, Purchase([new(_basic)]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await NotifyAsync(first, id: 30, tamper: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NotifyAsync(first, id: 30, account: "999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NotifyAsync(first, id: 30, direction: "out")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await NotifyAsync(first, id: 30, amount: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(first, id: 30)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await NotifyAsync(second, id: 30)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.MemberMemberships.ToListAsync());
        Assert.Equal(OrderStatus.Pending, (await db.Orders.SingleAsync(order => order.OrderNumber == second.OrderNumber)).OrderStatus);
    }

    [Fact]
    public async Task FailedProvisioningRollsBackSettlementAndCanBeRetried()
    {
        using var client = Client("buyer", "Customer");
        var purchase = await CreateAsync(client, Purchase([new(_basic), new(_combo)]));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER test_reject_membership BEFORE INSERT ON MemberMemberships
            BEGIN
                SELECT RAISE(ABORT, 'test provisioning failure');
            END;
            """);

        Assert.Equal(HttpStatusCode.InternalServerError, (await NotifyAsync(purchase, id: 35)).StatusCode);
        Assert.Equal(OrderStatus.Pending, (await db.Orders.AsNoTracking().SingleAsync()).OrderStatus);
        Assert.Equal(PaymentStatus.Pending, (await db.PaymentTransactions.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await db.MemberMemberships.ToListAsync());
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Equal(["Customer", "PT"], await RolesAsync(db, "buyer"));

        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_reject_membership");
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(purchase, id: 35)).StatusCode);
        Assert.Equal(3, await db.MemberMemberships.CountAsync());
    }

    [Fact]
    public async Task ExpirationIsServerAuthoritativeAndTimelyTransferSurvivesDelayedDelivery()
    {
        using var client = Client("buyer", "Customer");
        var purchase = await CreateAsync(client, Purchase([new(_basic)]));
        _clock.Now = purchase.ExpiresAt.AddMinutes(1);
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        var expired = await ReadAsync(await client.GetAsync($"{Route}/{purchase.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Expired", expired.OrderStatus);
        Assert.Equal("Cancelled", expired.PaymentStatus);
        Assert.Equal("MSG-24", expired.MessageCode);
        Assert.Null(expired.VietQr);
        Assert.Equal(HttpStatusCode.Conflict, (await NotifyAsync(purchase, id: 40, at: _clock.Now)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(purchase, id: 41, at: purchase.CreatedAt.AddMinutes(3))).StatusCode);
        Assert.Equal("Paid", (await ReadAsync(await client.GetAsync($"{Route}/{purchase.OrderNumber}"), HttpStatusCode.OK)).OrderStatus);
    }

    [Fact]
    public async Task CashRequiresExactReceiptByStaffAtPurchasersGymAndIsAuditedOnce()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        using var otherStaff = Client("other-staff", "Staff");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        var route = $"/api/staff/membership-purchases/{purchase.OrderNumber}/confirm-cash";
        var receipt = new ConfirmMembershipCashRequest(purchase.AmountDue);
        Assert.Equal(TimeSpan.FromHours(24), purchase.ExpiresAt - purchase.CreatedAt);
        Assert.Null(purchase.VietQr);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync(route, receipt)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherStaff.PostAsJsonAsync(route, receipt)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(1))).StatusCode);
        Assert.Equal("Paid", (await ReadAsync(await staff.PostAsJsonAsync(route, receipt), HttpStatusCode.OK)).OrderStatus);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(route, receipt)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal("staff", (await db.AuditLogs.SingleAsync()).ActorUserId);
        Assert.Single(await db.MemberMemberships.ToListAsync());
        Assert.Single(await db.Invoices.ToListAsync());
    }

    [Theory]
    [InlineData(17, null)]
    [InlineData(null, 2)]
    public async Task LegacyCheckoutSnapshotPreservesThePurchasedDuration(int? days, int? months)
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var component = await db.PackageComponents.SingleAsync(row => row.PackageId == _basic);
        var item = await db.OrderItems.SingleAsync();
        item.ComponentTermsSnapshot = JsonSerializer.Serialize(new[]
        {
            new { component.Id, Type = component.ComponentType, DurationDays = days, DurationMonths = months, component.SessionCount }
        });
        await db.SaveChangesAsync();

        var route = $"/api/staff/membership-purchases/{purchase.OrderNumber}/confirm-cash";
        using var response = await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(purchase.AmountDue));
        Assert.Equal("Paid", (await ReadAsync(response, HttpStatusCode.OK)).OrderStatus);
        var entitlement = await db.MemberMemberships.SingleAsync();
        var expectedEnd = days.HasValue ? Today.AddDays(days.Value - 1) : Today.AddMonths(months!.Value).AddDays(-1);
        Assert.Equal(expectedEnd, entitlement.EndDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiKeyWebhookRequiresExactKeyAndCannotBypassConfiguredHmac(bool useHmac)
    {
        const string key = "sepay-checkout-test-key";
        _app.Configuration["MembershipPayments:ApiKey"] = key;
        if (!useHmac)
            _app.Configuration["MembershipPayments:WebhookSecret"] = "";

        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        Assert.NotNull(purchase.VietQr);

        using var invalid = await NotifyAsync(purchase, id: 50, authorization: $"Apikey prefix-{key}");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        using var valid = await NotifyAsync(purchase, id: 50, authorization: $"Apikey {key}");
        Assert.Equal(useHmac ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, valid.StatusCode);
        if (useHmac)
        {
            using var signed = await NotifyAsync(purchase, id: 50);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        }

        var paid = await ReadAsync(await buyer.GetAsync($"{Route}/{purchase.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);
    }

    private async Task<HttpResponseMessage> NotifyAsync(
        MembershipPurchaseResponse purchase, long id, decimal? amount = null, bool tamper = false,
        string account = BankAccount, string direction = "in", DateTimeOffset? at = null, string? authorization = null)
    {
        using var client = Client();
        var date = (at ?? purchase.CreatedAt.AddSeconds(1)).ToOffset(TimeSpan.FromHours(7));
        var code = purchase.VietQr!.TransferContent;
        var body = JsonSerializer.Serialize(new
        {
            id, transactionDate = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            accountNumber = account, content = $"BANK {code} PAYMENT", transferType = direction,
            transferAmount = amount ?? purchase.AmountDue
        });
        var timestamp = _clock.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute)
        {
            Content = new StringContent(tamper ? body + " " : body, Encoding.UTF8, "application/json")
        };
        if (authorization is null)
        {
            request.Headers.Add("X-SePay-Timestamp", timestamp);
            request.Headers.Add("X-SePay-Signature", $"sha256={Convert.ToHexString(signature).ToLowerInvariant()}");
        }
        else
        {
            request.Headers.Add("Authorization", authorization);
        }
        return await client.SendAsync(request);
    }

    private async Task RunJobAsync(Func<IMembershipPurchaseService, CancellationToken, Task> run)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        await run(scope.ServiceProvider.GetRequiredService<IMembershipPurchaseService>(), CancellationToken.None);
    }

    private HttpClient Client(string? user = null, string? role = null)
    {
        var client = _app.GetTestClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add("Test-User", user);
            client.DefaultRequestHeaders.Add("Test-Role", role);
        }
        return client;
    }

    private static MembershipPurchaseRequest Purchase(
        IReadOnlyList<MembershipPurchaseItemRequest> items, string method = MembershipPaymentMethods.VietQr) => new(items, method);

    private static async Task<MembershipPurchaseResponse> CreateAsync(HttpClient client, MembershipPurchaseRequest request) =>
        await ReadAsync(await client.PostAsJsonAsync(Route, request), HttpStatusCode.Created);

    private static async Task<MembershipPurchaseResponse> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. {body}");
            return JsonSerializer.Deserialize<MembershipPurchaseResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }

    private static Package Plan(string name, decimal price, PackageStatus status) =>
        new() { Name = name, Price = price, PackageStatus = status,
            Components = [new PackageComponent { ComponentType = ComponentType.GYM, DurationDays = 30 }] };

    private static ApplicationUser User(string id, Gym gym, ApplicationRole role, ApplicationRole? additional = null,
        AccountStatus status = AccountStatus.Active)
    {
        var user = new ApplicationUser
        {
            UserId = id, FullName = id, Gym = gym, Email = $"{id}@example.test", PasswordHash = "test",
            AccountStatus = status, UserRoles = [new ApplicationUserRole { Role = role }]
        };
        if (additional is not null)
            user.UserRoles.Add(new ApplicationUserRole { Role = additional });
        return user;
    }

    private static Task<List<string>> RolesAsync(VfmsDbContext db, string id) => db.ApplicationUserRoles
        .Where(role => role.UserId == id).Select(role => role.Role.RoleName).OrderBy(name => name).ToListAsync();

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.ToString()),
                new(ClaimTypes.Role, Request.Headers["Test-Role"].ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
        }
    }
}
