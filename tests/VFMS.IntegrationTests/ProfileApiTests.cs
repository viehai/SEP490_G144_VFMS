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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFMS.API.Controllers;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Users;
using VFMS.Contracts.Users.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class ProfileApiTests : IAsyncLifetime
{
    private const string ProfileRoute = "/api/users/me";
    private const string ChangePasswordRoute = "/api/users/me/change-password";
    private const string UserIdHeader = "Test-UserId";
    private const string RoleHeader = "Test-Role";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private WebApplication _app = null!;
    private int _gymId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        builder.Services.AddValidatorsFromAssemblyContaining<WalkInCustomerRequestValidator>();

        var dbOptions = new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        builder.Services.AddScoped<VfmsDbContext>(_ => new SqliteTestContext(dbOptions));
        builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
        builder.Services.AddScoped<IUserAdministrationRepository, UserAdministrationRepository>();
        builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();

        var mapsterConfig = new TypeAdapterConfig();
        mapsterConfig.Scan(typeof(UserAdministrationMapping).Assembly);
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

        var gym = new Gym { GymName = "Profile test gym" };
        db.Gyms.Add(gym);
        db.ApplicationRoles.AddRange(
            new ApplicationRole { RoleName = "Customer" },
            new ApplicationRole { RoleName = "Member" },
            new ApplicationRole { RoleName = "Staff" },
            new ApplicationRole { RoleName = "Manager" });
        await db.SaveChangesAsync();
        _gymId = gym.Id;
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("Customer", "scan")]
    [InlineData("Member", "scan")]
    [InlineData("Member", "check-in")]
    [InlineData("Member", "check-out")]
    [InlineData("Admin", "scan")]
    public async Task GateEndpointsRequireAStaffOrManagerSession(string role, string action)
    {
        using var client = CreateClient("viewer", role);
        using var response = await client.PostAsJsonAsync($"/api/kiosk/{action}", new { qrToken = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanChangeRolesLockAndUnlockFromTheAccountList()
    {
        await SeedUserAsync("operator", "Admin", "operator@example.test", "0909111101", ["Staff"]);
        await SeedUserAsync("target", "Nhân viên", "target@example.test", "0909111102", ["Staff"], softDeletedRoles: ["Manager"]);
        using var client = CreateClient("operator", "Admin");
        const string path = "/api/admin/users/target";
        var roles = await ReadJsonAsync<UserAccountResponse>(await client.PutAsJsonAsync(path + "/roles",
            new UpdateUserRolesRequest(["Manager"], ["Staff"])), HttpStatusCode.OK);
        Assert.Equal("Manager", Assert.Single(roles.Roles));
        using var invalid = await client.PatchAsJsonAsync(path + "/lock", new LockUserRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        const string reason = "Xác minh tài khoản tại quầy";
        var locked = await ReadJsonAsync<UserAccountResponse>(await client.PatchAsJsonAsync(path + "/lock",
            new LockUserRequest(reason)), HttpStatusCode.OK);
        Assert.Equal("Locked", locked.AccountStatus);
        var page = await client.GetFromJsonAsync<PagedUsersResponse>("/api/admin/users?accountStatus=Locked");
        var account = Assert.Single(page!.Items);
        Assert.Equal(reason, account.LockReason);
        Assert.Equal("Manager", Assert.Single(account.Roles));
        var unlocked = await ReadJsonAsync<UserAccountResponse>(await client.PatchAsJsonAsync(path + "/unlock", new {}), HttpStatusCode.OK);
        Assert.Equal("Active", unlocked.AccountStatus);
        Assert.Null(unlocked.LockReason);
    }

    [Fact]
    public async Task AdminMustExplicitlyChooseOneRoleForALegacyAccount()
    {
        string[] previousRoles = ["Staff", "Manager"];
        await SeedUserAsync("operator", "Admin", "operator@example.test", "0909111101", ["Staff"]);
        await SeedUserAsync("target", "Nhân viên", "target@example.test", "0909111102", previousRoles);
        using var client = CreateClient("operator", "Admin");
        const string path = "/api/admin/users/target/roles";

        foreach (string[] invalid in new string[][] { [], ["Staff", "Manager"], ["Staff", "Staff"] })
        {
            using var rejected = await client.PutAsJsonAsync(path, new UpdateUserRolesRequest(invalid, previousRoles));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var stale = await client.PutAsJsonAsync(path, new UpdateUserRolesRequest(["Manager"], ["Staff"]));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updated = await ReadJsonAsync<UserAccountResponse>(await client.PutAsJsonAsync(path,
            new UpdateUserRolesRequest(["Staff"], previousRoles)), HttpStatusCode.OK);
        Assert.Equal("Staff", Assert.Single(updated.Roles));
        var page = await client.GetFromJsonAsync<PagedUsersResponse>("/api/admin/users?search=target@example.test");
        Assert.Equal("Staff", Assert.Single(Assert.Single(page!.Items).Roles));
    }

    [Fact]
    public async Task UnauthenticatedGetMyProfile_ReturnsUnauthorized()
    {
        using var client = CreateClient(userId: null);

        using var response = await client.GetAsync(ProfileRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProfile_AuthenticatedStaffUser_ReturnsRealDatabaseValues()
    {
        var createdAt = new DateTime(2025, 2, 14, 9, 30, 0, DateTimeKind.Utc);
        await SeedUserAsync(
            userId: "linh-staff-id",
            fullName: "Linh Staff",
            email: "linh.staff@vfms.test",
            phoneNumber: "0909123456",
            activeRoles: ["Staff"],
            createdAt: createdAt);

        using var client = CreateClient("linh-staff-id", "Staff");

        var profile = await ReadJsonAsync<UserProfileResponse>(await client.GetAsync(ProfileRoute), HttpStatusCode.OK);

        Assert.Equal("linh-staff-id", profile.UserId);
        Assert.Equal("Linh Staff", profile.FullName);
        Assert.Equal("linh.staff@vfms.test", profile.Email);
        Assert.Equal("0909123456", profile.PhoneNumber);
        Assert.Equal(new[] { "Staff" }, profile.Roles);
        Assert.Equal("Active", profile.AccountStatus);
        Assert.Equal(createdAt, profile.CreatedAt);
        Assert.Null(profile.DateOfBirth);
    }

    [Fact]
    public async Task GetMyProfile_AuthenticatedMemberWithPhysicalProfile_ReturnsDateOfBirth()
    {
        var expectedDob = new DateOnly(1998, 8, 21);
        await SeedUserAsync(
            userId: "member-dob-id",
            fullName: "Member Minh",
            email: "member.minh@vfms.test",
            phoneNumber: "0912345670",
            activeRoles: ["Member"],
            dateOfBirth: expectedDob);

        using var client = CreateClient("member-dob-id", "Member");

        var profile = await ReadJsonAsync<UserProfileResponse>(await client.GetAsync(ProfileRoute), HttpStatusCode.OK);

        Assert.Equal("member-dob-id", profile.UserId);
        Assert.Equal(new[] { "Member" }, profile.Roles);
        Assert.Equal(expectedDob, profile.DateOfBirth);
    }

    [Fact]
    public async Task GetMyProfile_ExcludesSoftDeletedRoles()
    {
        await SeedUserAsync(
            userId: "soft-role-user",
            fullName: "Soft Role User",
            email: "softrole@vfms.test",
            phoneNumber: "0912345671",
            activeRoles: ["Staff"],
            softDeletedRoles: ["Customer"]);

        using var client = CreateClient("soft-role-user", "Staff");

        var profile = await ReadJsonAsync<UserProfileResponse>(await client.GetAsync(ProfileRoute), HttpStatusCode.OK);

        Assert.Equal(new[] { "Staff" }, profile.Roles);
        Assert.DoesNotContain("Customer", profile.Roles);
    }

    [Fact]
    public async Task GetMyProfile_ReflectsRoleChangeInDatabase()
    {
        await SeedUserAsync(
            userId: "role-change-user",
            fullName: "Promoted User",
            email: "promoted@vfms.test",
            phoneNumber: "0912345672",
            activeRoles: ["Customer"]);

        using var initialClient = CreateClient("role-change-user", "Customer");
        var before = await ReadJsonAsync<UserProfileResponse>(await initialClient.GetAsync(ProfileRoute), HttpStatusCode.OK);
        Assert.Equal(new[] { "Customer" }, before.Roles);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var customerLink = await db.ApplicationUserRoles
                .Include(ur => ur.Role)
                .SingleAsync(ur => ur.UserId == "role-change-user" && ur.Role.RoleName == "Customer");
            customerLink.IsDeleted = true;

            var staffRole = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "Staff");
            db.ApplicationUserRoles.Add(new ApplicationUserRole
            {
                UserId = "role-change-user",
                RoleId = staffRole.Id
            });
            await db.SaveChangesAsync();
        }

        using var freshClient = CreateClient("role-change-user", "Staff");
        var after = await ReadJsonAsync<UserProfileResponse>(await freshClient.GetAsync(ProfileRoute), HttpStatusCode.OK);
        Assert.Equal(new[] { "Staff" }, after.Roles);
    }

    [Fact]
    public async Task ProfileOwnership_QueryParametersCannotReadOrModifyAnotherUser()
    {
        await SeedUserAsync(
            userId: "user-a",
            fullName: "User Alpha",
            email: "alpha@vfms.test",
            phoneNumber: "0911111110",
            activeRoles: ["Customer"]);
        await SeedUserAsync(
            userId: "user-b",
            fullName: "User Beta",
            email: "beta@vfms.test",
            phoneNumber: "0922222220",
            activeRoles: ["Staff"]);

        using var clientA = CreateClient("user-a", "Customer");

        var readProfile = await ReadJsonAsync<UserProfileResponse>(
            await clientA.GetAsync($"{ProfileRoute}?userId=user-b&email=beta@vfms.test"),
            HttpStatusCode.OK);
        Assert.Equal("user-a", readProfile.UserId);
        Assert.Equal("User Alpha", readProfile.FullName);
        Assert.Equal("alpha@vfms.test", readProfile.Email);

        var updatedProfile = await ReadJsonAsync<UserProfileResponse>(
            await clientA.PutAsJsonAsync(
                $"{ProfileRoute}?userId=user-b",
                new UpdateUserProfileRequest("Alpha Renamed", "0911111199")),
            HttpStatusCode.OK);
        Assert.Equal("user-a", updatedProfile.UserId);
        Assert.Equal("Alpha Renamed", updatedProfile.FullName);
        Assert.Equal("0911111199", updatedProfile.PhoneNumber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var userAInDb = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "user-a");
        var userBInDb = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "user-b");

        Assert.Equal("Alpha Renamed", userAInDb.FullName);
        Assert.Equal("0911111199", userAInDb.PhoneNumber);
        Assert.Equal("User Beta", userBInDb.FullName);
        Assert.Equal("beta@vfms.test", userBInDb.Email);
        Assert.Equal("0922222220", userBInDb.PhoneNumber);
    }

    [Fact]
    public async Task GetMyProfile_ResponseDoesNotExposeSensitiveFields()
    {
        const string resetTokenHash = "secret-reset-token-hash-value";
        const string setupTokenHash = "secret-setup-token-hash-value";
        var storedPasswordHash = await SeedUserAsync(
            userId: "secure-user",
            fullName: "Secure User",
            email: "secure@vfms.test",
            phoneNumber: "0912345673",
            activeRoles: ["Staff"],
            rawPassword: "SecurePass@123",
            resetTokenHash: resetTokenHash,
            passwordSetupTokenHash: setupTokenHash);

        using var client = CreateClient("secure-user", "Staff");

        using var response = await client.GetAsync(ProfileRoute);
        var rawJson = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("passwordHash", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resetTokenHash", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordSetupTokenHash", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(storedPasswordHash, rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain(resetTokenHash, rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain(setupTokenHash, rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMyProfile_PersistsChangesAndPreservesReadOnlyFields()
    {
        await SeedUserAsync(
            userId: "persist-user",
            fullName: "Original Name",
            email: "persist@vfms.test",
            phoneNumber: "0933333330",
            activeRoles: ["Staff"]);

        using var client = CreateClient("persist-user", "Staff");

        var updated = await ReadJsonAsync<UserProfileResponse>(
            await client.PutAsJsonAsync(
                ProfileRoute,
                new UpdateUserProfileRequest("Persisted Name", "0933333399", "https://example.com/avatar.png")),
            HttpStatusCode.OK);

        Assert.Equal("Persisted Name", updated.FullName);
        Assert.Equal("0933333399", updated.PhoneNumber);
        Assert.Equal("https://example.com/avatar.png", updated.AvatarUrl);
        Assert.Equal("persist@vfms.test", updated.Email);
        Assert.Equal(new[] { "Staff" }, updated.Roles);
        Assert.Equal("Active", updated.AccountStatus);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var userInDb = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "persist-user");
            Assert.Equal("Persisted Name", userInDb.FullName);
            Assert.Equal("0933333399", userInDb.PhoneNumber);
            Assert.Equal("persist@vfms.test", userInDb.Email);
            Assert.Equal(AccountStatus.Active, userInDb.AccountStatus);
        }

        var reloaded = await ReadJsonAsync<UserProfileResponse>(await client.GetAsync(ProfileRoute), HttpStatusCode.OK);
        Assert.Equal("Persisted Name", reloaded.FullName);
        Assert.Equal("0933333399", reloaded.PhoneNumber);
        Assert.Equal("persist@vfms.test", reloaded.Email);
        Assert.Equal(new[] { "Staff" }, reloaded.Roles);
        Assert.Equal("Active", reloaded.AccountStatus);
    }

    [Fact]
    public async Task UpdateMyProfile_DuplicatePhoneNumber_ReturnsConflict()
    {
        await SeedUserAsync(
            userId: "dup-user-a",
            fullName: "User A",
            email: "dupa@vfms.test",
            phoneNumber: "0944444440",
            activeRoles: ["Customer"]);
        await SeedUserAsync(
            userId: "dup-user-b",
            fullName: "User B",
            email: "dupb@vfms.test",
            phoneNumber: "0955555550",
            activeRoles: ["Customer"]);

        using var clientA = CreateClient("dup-user-a", "Customer");

        using var response = await clientA.PutAsJsonAsync(
            ProfileRoute,
            new UpdateUserProfileRequest("User A Updated", "0955555550"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var userAInDb = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "dup-user-a");
        Assert.Equal("User A", userAInDb.FullName);
        Assert.Equal("0944444440", userAInDb.PhoneNumber);
    }

    [Fact]
    public async Task UpdateMyProfile_PersistsDateOfBirthToDatabaseAndRejectsFutureDate()
    {
        await SeedUserAsync(
            userId: "dob-user",
            fullName: "Birthday User",
            email: "dob@vfms.test",
            phoneNumber: "0977777770",
            activeRoles: ["Member"]);

        using var client = CreateClient("dob-user", "Member");

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1);
        using var invalidResponse = await client.PutAsJsonAsync(
            ProfileRoute,
            new UpdateUserProfileRequest("Birthday User", "0977777770", null, futureDate));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var expectedDob = new DateOnly(2004, 2, 12);
        var updated = await ReadJsonAsync<UserProfileResponse>(
            await client.PutAsJsonAsync(
                ProfileRoute,
                new UpdateUserProfileRequest("Birthday User", "0977777770", null, expectedDob)),
            HttpStatusCode.OK);
        Assert.Equal(expectedDob, updated.DateOfBirth);

        var reloaded = await ReadJsonAsync<UserProfileResponse>(
            await client.GetAsync(ProfileRoute),
            HttpStatusCode.OK);
        Assert.Equal(expectedDob, reloaded.DateOfBirth);

        var cleared = await ReadJsonAsync<UserProfileResponse>(
            await client.PutAsJsonAsync(
                ProfileRoute,
                new UpdateUserProfileRequest("Birthday User", "0977777770", null, null)),
            HttpStatusCode.OK);
        Assert.Null(cleared.DateOfBirth);

        var reloadedAfterClear = await ReadJsonAsync<UserProfileResponse>(
            await client.GetAsync(ProfileRoute),
            HttpStatusCode.OK);
        Assert.Null(reloadedAfterClear.DateOfBirth);
    }

    [Fact]
    public async Task ChangeMyPassword_RejectsWrongCurrentPasswordAndPersistsValidChange()
    {
        var originalHash = await SeedUserAsync(
            userId: "password-user",
            fullName: "Password User",
            email: "pwd@vfms.test",
            phoneNumber: "0966666660",
            activeRoles: ["Member"],
            rawPassword: "OldPassword@123");

        using var client = CreateClient("password-user", "Member");

        using var invalidResponse = await client.PutAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest("WrongPass@123", "NewPassword@456"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var unchanged = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "password-user");
            Assert.Equal(originalHash, unchanged.PasswordHash);
        }

        var validResult = await ReadJsonAsync<bool>(
            await client.PutAsJsonAsync(
                ChangePasswordRoute,
                new ChangePasswordRequest("OldPassword@123", "NewPassword@456")),
            HttpStatusCode.OK);
        Assert.True(validResult);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var updated = await db.ApplicationUsers.AsNoTracking().SingleAsync(u => u.UserId == "password-user");

            Assert.NotEqual(originalHash, updated.PasswordHash);
            Assert.True(hasher.Verify("NewPassword@456", updated.PasswordHash));
            Assert.False(hasher.Verify("OldPassword@123", updated.PasswordHash));
        }
    }

    private HttpClient CreateClient(string? userId, string? role = null)
    {
        var client = _app.GetTestClient();
        if (userId is not null)
            client.DefaultRequestHeaders.Add(UserIdHeader, userId);
        if (role is not null)
            client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    private async Task<string> SeedUserAsync(
        string userId,
        string fullName,
        string email,
        string phoneNumber,
        IReadOnlyCollection<string> activeRoles,
        IReadOnlyCollection<string>? softDeletedRoles = null,
        DateTime? createdAt = null,
        DateOnly? dateOfBirth = null,
        string rawPassword = "TestPassword@123",
        string? resetTokenHash = null,
        string? passwordSetupTokenHash = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var passwordHash = hasher.Hash(rawPassword);
        var user = new ApplicationUser
        {
            UserId = userId,
            GymId = _gymId,
            FullName = fullName,
            Email = email,
            PhoneNumber = phoneNumber,
            PasswordHash = passwordHash,
            ResetTokenHash = resetTokenHash,
            PasswordSetupTokenHash = passwordSetupTokenHash,
            AccountStatus = AccountStatus.Active,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.ApplicationUsers.Add(user);

        var roleNames = activeRoles
            .Concat(softDeletedRoles ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rolesByName = await db.ApplicationRoles
            .Where(r => roleNames.Contains(r.RoleName))
            .ToDictionaryAsync(r => r.RoleName, StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in activeRoles)
        {
            db.ApplicationUserRoles.Add(new ApplicationUserRole
            {
                UserId = userId,
                RoleId = rolesByName[roleName].Id,
                IsDeleted = false
            });
        }

        if (softDeletedRoles is not null)
        {
            foreach (var roleName in softDeletedRoles)
            {
                db.ApplicationUserRoles.Add(new ApplicationUserRole
                {
                    UserId = userId,
                    RoleId = rolesByName[roleName].Id,
                    IsDeleted = true
                });
            }
        }

        if (dateOfBirth.HasValue)
        {
            db.PhysicalProfiles.Add(new PhysicalProfile
            {
                MemberUserId = userId,
                DateOfBirth = dateOfBirth.Value,
                IsDeleted = false
            });
        }

        await db.SaveChangesAsync();

        if (createdAt.HasValue)
        {
            await db.ApplicationUsers
                .Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.CreatedAt, createdAt.Value));
        }

        return passwordHash;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. {body}");
            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            Assert.NotNull(result);
            return result!;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
                return Task.FromResult(AuthenticateResult.NoResult());

            var userId = userIdValues.ToString();
            if (string.IsNullOrWhiteSpace(userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            if (Request.Headers.TryGetValue(RoleHeader, out var roleValues) &&
                !string.IsNullOrWhiteSpace(roleValues.ToString()))
            {
                claims.Add(new Claim(ClaimTypes.Role, roleValues.ToString()));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class SqliteTestContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
            modelBuilder.Entity<Voucher>().Property(voucher => voucher.Code).UseCollation("NOCASE");
        }
    }
}
