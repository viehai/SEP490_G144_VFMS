using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFMS.API.Controllers;
using VFMS.API.Extensions;
using VFMS.Contracts.Auth.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class PromotionApiTests : IAsyncLifetime
{
    private const string Route = "/api/manager/promotions";
    private const string ManagerId = "promotion-manager";
    private const string CustomerId = "promotion-customer";
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly TestClock _clock = new();
    private readonly SelectCounter _selectCounter = new();
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _connection.CreateCollation("Latin1_General_100_CI_AS", (left, right) =>
            string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ManagerMembershipPlansController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        var options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection)
            .AddInterceptors(_selectCounter).Options;
        builder.Services.AddScoped<VfmsDbContext>(_ => new TestContext(options));
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddPromotionModule();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
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
        var gym = new Gym { GymName = "Promotions gym" };
        db.ApplicationUsers.AddRange(User(ManagerId, gym), User(CustomerId, gym));
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
    [InlineData("Staff", HttpStatusCode.Forbidden)]
    [InlineData("PT", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task EveryManagementEndpointRequiresManager(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        using var list = await client.GetAsync(Route);
        using var detail = await client.GetAsync($"{Route}/1");
        using var create = await client.PostAsJsonAsync(Route, Request());
        using var update = await client.PutAsJsonAsync($"{Route}/1", Request());
        using var toggle = await client.PatchAsJsonAsync($"{Route}/1/enabled", new { isEnabled = false });
        Assert.All(new[] { list, detail, create, update, toggle }, response => Assert.Equal(expected, response.StatusCode));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Empty(await db.Vouchers.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task EmptyListIsSuccessfulAndDoesNotAuditReads()
    {
        using var manager = Client();
        Assert.Empty((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Percentage", 20, 100000)]
    [InlineData("Fixed", 50000, null)]
    public async Task ManagerCreatesAndReadsCanonicalPromotionWithCompleteConfiguration(string type, int value, int? cap)
    {
        using var manager = Client();
        var request = Request("  New campaign  ", "  save20  ", type, value, cap);
        var created = await Create(manager, request);
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal("New campaign", created.GetProperty("name").GetString());
        Assert.Equal("SAVE20", created.GetProperty("code").GetString());
        Assert.Equal(type, created.GetProperty("discountType").GetString());
        Assert.Equal(value, created.GetProperty("discountValue").GetDecimal());
        Assert.Equal(200000, created.GetProperty("minimumSpend").GetDecimal());
        Assert.Equal(cap, created.GetProperty("maxDiscountAmount").ValueKind == JsonValueKind.Null
            ? null : created.GetProperty("maxDiscountAmount").GetInt32());
        Assert.Equal(20, created.GetProperty("maxRedemptions").GetInt32());
        Assert.Equal(0, created.GetProperty("usedCount").GetInt32());
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.True(created.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(request["startAt"], created.GetProperty("startAt").GetDateTime());
        Assert.Equal(DateTimeKind.Utc, created.GetProperty("startAt").GetDateTime().Kind);

        var detail = await Detail(manager, id);
        Assert.Equal(created.GetRawText(), detail.GetRawText());
        var listed = Assert.Single((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
        Assert.Equal(created.GetRawText(), listed.GetRawText());
        Assert.False(detail.TryGetProperty("createdByUserId", out _));
        Assert.False(detail.TryGetProperty("maxRedemptionsPerUser", out _));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.Vouchers.SingleAsync();
        Assert.Equal(type == "Percentage" ? DiscountType.PERCENT : DiscountType.FIXED, stored.DiscountType);
        Assert.Equal(ManagerId, stored.CreatedByUserId);
        Assert.Empty(await db.VoucherRedemptions.ToListAsync());
        Assert.Empty(await db.Orders.ToListAsync());
        var audit = Assert.Single(await db.AuditLogs.ToListAsync());
        AssertAudit(audit, id);
        Assert.Equal(AuditActionType.Create, audit.ActionType);
        Assert.Null(audit.OldValue);
        Assert.Equal("SAVE20", JsonSerializer.Deserialize<JsonElement>(audit.NewValue!).GetProperty("Code").GetString());
    }

    [Theory]
    [InlineData("name", null)]
    [InlineData("name", "  ")]
    [InlineData("code", null)]
    [InlineData("code", " ")]
    [InlineData("discountType", null)]
    [InlineData("discountType", "Unknown")]
    [InlineData("discountType", 99)]
    [InlineData("discountValue", null)]
    [InlineData("discountValue", 0)]
    [InlineData("discountValue", -1)]
    [InlineData("discountValue", 101)]
    [InlineData("minimumSpend", -1)]
    [InlineData("maxDiscountAmount", null)]
    [InlineData("maxDiscountAmount", 0)]
    [InlineData("maxDiscountAmount", -1)]
    [InlineData("maxRedemptions", 0)]
    [InlineData("maxRedemptions", -1)]
    [InlineData("startAt", null)]
    [InlineData("endAt", null)]
    [InlineData("isEnabled", null)]
    public async Task InvalidConfigurationIsRejectedWithoutPartialMutation(string field, object? value)
    {
        var request = Request();
        if (value is null) request.Remove(field);
        else request[field] = value;
        using var manager = Client();
        using var response = await manager.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Empty(await db.Vouchers.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EndMustBeStrictlyAfterStart(int minutes)
    {
        var request = Request();
        request["endAt"] = ((DateTime)request["startAt"]!).AddMinutes(minutes);
        using var manager = Client();
        using var response = await manager.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-11-13T12:00:00")]
    [InlineData("0001-01-01T00:00:00Z")]
    public async Task UnspecifiedOrDefaultTimestampsAreRejected(string start)
    {
        var request = Request();
        request["startAt"] = start;
        using var manager = Client();
        using var response = await manager.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("SAVE20")]
    [InlineData("save20")]
    [InlineData("  SaVe20  ")]
    public async Task DuplicateNormalizedCodeReturnsConflictAndDoesNotAudit(string duplicate)
    {
        using var manager = Client();
        await Create(manager);
        var problem = await Body(await manager.PostAsJsonAsync(Route, Request(code: duplicate)), HttpStatusCode.Conflict);
        Assert.Equal("PROMOTION_CODE_EXISTS", problem.GetProperty("code").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.Vouchers.ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ManagerUpdatesConfigurationAndCannotReuseAnotherPromotionsCode()
    {
        using var manager = Client();
        var first = await Create(manager);
        var id = first.GetProperty("id").GetInt32();
        await Create(manager, Request(code: "OTHER"));
        var updated = await Body(await manager.PutAsJsonAsync($"{Route}/{id}",
            Request("Updated", " SAVE20 ", "Fixed", 60000, null)), HttpStatusCode.OK);
        Assert.Equal("Updated", updated.GetProperty("name").GetString());
        Assert.Equal("Fixed", updated.GetProperty("discountType").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("maxDiscountAmount").ValueKind);

        using var rejected = await manager.PutAsJsonAsync($"{Route}/{id}", Request(code: "other"));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(updated.GetRawText(), (await Detail(manager, id)).GetRawText());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var logs = await db.AuditLogs.Where(log => log.EntityId == id).OrderBy(log => log.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        AssertAudit(logs[1], id);
        Assert.Equal(AuditActionType.Update, logs[1].ActionType);
        Assert.Equal("Promotion", JsonSerializer.Deserialize<JsonElement>(logs[1].OldValue!).GetProperty("Name").GetString());
        Assert.Equal("Updated", JsonSerializer.Deserialize<JsonElement>(logs[1].NewValue!).GetProperty("Name").GetString());
    }

    [Fact]
    public async Task NoOpUpdateOrToggleAndReadsDoNotCreateAuditNoise()
    {
        using var manager = Client();
        var id = (await Create(manager)).GetProperty("id").GetInt32();
        await Body(await manager.PutAsJsonAsync($"{Route}/{id}", Request(code: " save20 ")), HttpStatusCode.OK);
        await Body(await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = true }), HttpStatusCode.OK);
        await Detail(manager, id);
        await Body(await manager.GetAsync(Route), HttpStatusCode.OK);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UnknownPromotionReturnsNotFoundAndPermanentDeleteIsNotExposed()
    {
        using var manager = Client();
        using var detail = await manager.GetAsync($"{Route}/999");
        using var update = await manager.PutAsJsonAsync($"{Route}/999", Request());
        using var toggle = await manager.PatchAsJsonAsync($"{Route}/999/enabled", new { isEnabled = false });
        using var delete = await manager.DeleteAsync($"{Route}/999");
        Assert.All(new[] { detail, update, toggle }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Contains(delete.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task EnableRequiresExplicitBooleanAndCannotAcceptDerivedStatusInstead()
    {
        using var manager = Client();
        var id = (await Create(manager)).GetProperty("id").GetInt32();
        using var missing = await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { status = "Active" });
        using var nullValue = await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = (bool?)null });
        using var invalid = await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = "Active" });
        Assert.All(new[] { missing, nullValue, invalid }, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal("Active", (await Detail(manager, id)).GetProperty("status").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().AuditLogs.ToListAsync());
    }

    private HttpClient Client(string? role = "Manager")
    {
        var client = _app.GetTestClient();
        if (role is not null) client.DefaultRequestHeaders.Add("Test-Role", role);
        return client;
    }

    private Dictionary<string, object?> Request(string name = "Promotion", string code = "SAVE20",
        string type = "Percentage", decimal value = 20, decimal? cap = 100000) => new()
    {
        ["name"] = name,
        ["code"] = code,
        ["discountType"] = type,
        ["discountValue"] = value,
        ["minimumSpend"] = 200000m,
        ["maxDiscountAmount"] = cap,
        ["maxRedemptions"] = 20,
        ["startAt"] = _clock.Now.UtcDateTime.AddDays(-1),
        ["endAt"] = _clock.Now.UtcDateTime.AddDays(1),
        ["isEnabled"] = true
    };

    private async Task<JsonElement> Create(HttpClient manager, Dictionary<string, object?>? request = null) =>
        await Body(await manager.PostAsJsonAsync(Route, request ?? Request()), HttpStatusCode.Created);

    private static async Task<JsonElement> Detail(HttpClient manager, int id) =>
        await Body(await manager.GetAsync($"{Route}/{id}"), HttpStatusCode.OK);

    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
    }

    private void AssertAudit(AuditLog audit, int id)
    {
        Assert.Equal(id, audit.EntityId);
        Assert.Equal(nameof(Voucher), audit.AuditEntity);
        Assert.Equal(ManagerId, audit.ActorUserId);
        Assert.NotEqual(default, audit.OccurredAt);
    }

    private static ApplicationUser User(string id, Gym gym) => new()
    {
        UserId = id, Gym = gym, FullName = id, Email = $"{id}@example.com",
        PasswordHash = "unused-test-password-hash", AccountStatus = AccountStatus.Active
    };

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier,
                role == "Manager" ? ManagerId : CustomerId), new(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 5, 0, 0, TimeSpan.Zero);
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

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
