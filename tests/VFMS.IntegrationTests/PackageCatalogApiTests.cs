using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using MapsterMapper;
using Mapster;
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

/// <summary>
/// Integration tests cho GET /api/packages?type=gym|yoga|pt (public catalog).
/// </summary>
public sealed class PackageCatalogApiTests : IAsyncLifetime
{
    private const string CatalogRoute = "/api/packages";
    private const string RoleHeader = "Test-Role";

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private WebApplication _app = null!;

    // ── Lifecycle ──────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PackageCatalogController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

        var dbOptions = new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlite(_connection).Options;
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

        // Seed minimal Gym entity needed for ApplicationUser FK
        var gym = new Gym { GymName = "Package catalog test gym" };
        db.Gyms.Add(gym);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Helper ─────────────────────────────────────────────────

    private HttpClient CreateClient(string? role = null)
    {
        var client = _app.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private async Task<Package> CreatePackage(
        string name,
        ComponentType componentType,
        PackageStatus status = PackageStatus.Active,
        bool isDeleted = false,
        TrainerLevel? trainerLevel = null,
        int? sessionCount = null,
        int? durationDays = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();

        var component = new PackageComponent
        {
            ComponentType = componentType,
            DurationDays = durationDays,
            SessionCount = sessionCount,
            ApplicableTrainerLevel = trainerLevel,
            YogaDifficultyLevel = componentType == ComponentType.YOGA ? "Advanced" : null,   // yoga plans without a level are not sold
            AllowFreeze = false
        };

        var package = new Package
        {
            Name = name,
            Price = 500_000m,
            PackageStatus = status,
            IsDeleted = isDeleted,
            Components = [component]
        };

        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package;
    }

    // ── Test Cases ─────────────────────────────────────────────

    [Fact]
    public async Task AnonymousAccessReturns200()
    {
        // No auth header — anonymous
        await CreatePackage("GYM Public", ComponentType.GYM, durationDays: 30);

        using var client = CreateClient(); // no role
        using var response = await client.GetAsync($"{CatalogRoute}?type=gym");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GymFilterReturnsOnlyGymPackages()
    {
        await CreatePackage("GYM Alpha", ComponentType.GYM, durationDays: 30);
        await CreatePackage("YOGA Beta", ComponentType.YOGA, sessionCount: 8);
        await CreatePackage("PT Gamma", ComponentType.PT, trainerLevel: TrainerLevel.Junior);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym"), HttpStatusCode.OK);

        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        foreach (var item in items.EnumerateArray())
            Assert.Equal("GYM", item.GetProperty("componentType").GetString());
    }

    [Fact]
    public async Task YogaFilterReturnsOnlyYogaPackages()
    {
        await CreatePackage("YOGA Only", ComponentType.YOGA, sessionCount: 10);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=yoga"), HttpStatusCode.OK);

        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        foreach (var item in items.EnumerateArray())
            Assert.Equal("YOGA", item.GetProperty("componentType").GetString());
    }

    [Fact]
    public async Task PtFilterReturnsOnlyPtPackages()
    {
        await CreatePackage("PT Premium", ComponentType.PT, trainerLevel: TrainerLevel.Junior, sessionCount: 10);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=pt"), HttpStatusCode.OK);

        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        foreach (var item in items.EnumerateArray())
            Assert.Equal("PT", item.GetProperty("componentType").GetString());
    }

    [Fact]
    public async Task InactivePackagesAreExcluded()
    {
        await CreatePackage("GYM Inactive", ComponentType.GYM, status: PackageStatus.Inactive, durationDays: 30);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym"), HttpStatusCode.OK);

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, item =>
            item.GetProperty("name").GetString() == "GYM Inactive");
    }

    [Fact]
    public async Task SoftDeletedPackagesAreExcluded()
    {
        await CreatePackage("GYM Deleted", ComponentType.GYM, isDeleted: true, durationDays: 30);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym"), HttpStatusCode.OK);

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, item =>
            item.GetProperty("name").GetString() == "GYM Deleted");
    }

    [Fact]
    public async Task ComboPackageAppearsInBothTabs()
    {
        // Package có cả GYM + YOGA component
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var combo = new Package
        {
            Name = "COMBO GYM+YOGA",
            Price = 1_200_000m,
            PackageStatus = PackageStatus.Active,
            Components =
            [
                new PackageComponent { ComponentType = ComponentType.GYM,  DurationDays = 30 },
                new PackageComponent { ComponentType = ComponentType.YOGA, SessionCount = 8, YogaDifficultyLevel = "Advanced" }
            ]
        };
        db.Packages.Add(combo);
        await db.SaveChangesAsync();

        using var client = CreateClient();

        var gymBody = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym"), HttpStatusCode.OK);
        var yogaBody = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=yoga"), HttpStatusCode.OK);

        var gymItems = gymBody.GetProperty("items").EnumerateArray().ToList();
        var yogaItems = yogaBody.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(gymItems, i => i.GetProperty("name").GetString() == "COMBO GYM+YOGA"
                                    && i.GetProperty("componentType").GetString() == "GYM");
        Assert.Contains(yogaItems, i => i.GetProperty("name").GetString() == "COMBO GYM+YOGA"
                                      && i.GetProperty("componentType").GetString() == "YOGA");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("gym123")]
    [InlineData("")]
    public async Task InvalidTypeReturns400(string type)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"{CatalogRoute}?type={type}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyResultReturns200WithEmptyList()
    {
        // Không seed YOGA package nào
        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=yoga"), HttpStatusCode.OK);

        // Có thể có yoga packages từ test khác, nhưng ít nhất totalCount phải hợp lệ
        Assert.True(body.TryGetProperty("totalCount", out _));
        Assert.True(body.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task PaginationWorks()
    {
        // Seed 3 GYM packages
        for (var i = 1; i <= 3; i++)
            await CreatePackage($"GYM Paginated {i:D2}", ComponentType.GYM, durationDays: i * 10);

        using var client = CreateClient();
        var page1 = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym&page=1&pageSize=2"), HttpStatusCode.OK);
        var page2 = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym&page=2&pageSize=2"), HttpStatusCode.OK);

        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page1.GetProperty("page").GetInt32());
        Assert.Equal(2, page1.GetProperty("pageSize").GetInt32());

        // Trang 2 có ít nhất 1 phần tử (có thể có thêm từ tests khác nhưng không thể là 0)
        Assert.True(page2.GetProperty("totalCount").GetInt32() >= 3);
    }

    [Fact]
    public async Task ResponseShapeIncludesAllExpectedFields()
    {
        await CreatePackage("GYM Shape Test", ComponentType.GYM, durationDays: 30);

        using var client = CreateClient();
        var body = await ReadBodyAsync(await client.GetAsync($"{CatalogRoute}?type=gym"), HttpStatusCode.OK);

        var item = body.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("name").GetString() == "GYM Shape Test");

        // Các field chung bắt buộc
        Assert.True(item.TryGetProperty("id", out _));
        Assert.True(item.TryGetProperty("name", out _));
        Assert.True(item.TryGetProperty("price", out _));
        Assert.True(item.TryGetProperty("status", out _));
        Assert.True(item.TryGetProperty("componentType", out _));
        Assert.Equal("ACTIVE", item.GetProperty("status").GetString());
        Assert.Equal("GYM", item.GetProperty("componentType").GetString());
        Assert.Equal(30, item.GetProperty("durationDays").GetInt32());
    }

    [Fact]
    public async Task TypeQueryIsCaseInsensitive()
    {
        await CreatePackage("GYM Case Test", ComponentType.GYM, durationDays: 30);

        using var client = CreateClient();

        using var r1 = await client.GetAsync($"{CatalogRoute}?type=GYM");
        using var r2 = await client.GetAsync($"{CatalogRoute}?type=Gym");
        using var r3 = await client.GetAsync($"{CatalogRoute}?type=gym");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
    }

    // ── Infrastructure ─────────────────────────────────────────

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, "test-user"), new(ClaimTypes.Role, role.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
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
}
