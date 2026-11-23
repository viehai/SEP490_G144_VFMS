using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFMS.API.Controllers;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Trainers;
using VFMS.Contracts.Users;
using VFMS.Contracts.Users.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class WalkInCustomerOnboardingApiTests : IAsyncLifetime
{
    private const string CreateCustomerRoute = "/api/users/walk-in-customer";
    private const string SetupPasswordRoute = "/api/auth/setup-password";
    private const string TrainerSetupRoute = "/api/trainers/setup-password";
    private const string FindPendingRoute = "/api/users/find-pending-customer";
    private const string LoginRoute = "/api/auth/login";
    private const string UserIdHeader = "Test-UserId";
    private const string RoleHeader = "Test-Role";
    private const string SetupUrl = "https://fitness.example.com/auth/setup-password";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly CapturingEmailService _emailService = new();
    private readonly ControllablePasswordHasher _hasher = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
    private WebApplication _app = null!;
    private DbContextOptions<VfmsDbContext> _dbOptions = null!;
    private string? _sqlDatabaseName;
    private int _gymId;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "VFMS-Test-SuperSecretKey-2026-MustBe32CharsMin!",
            ["JwtSettings:Issuer"] = "VFMS.API",
            ["JwtSettings:Audience"] = "VFMS.Client",
            ["PasswordReset:ResetUrl"] = "https://fitness.example/auth/reset-password",
            ["CustomerPasswordSetup:SetupUrl"] = SetupUrl,
            ["CustomerPasswordSetup:TokenLifetimeHours"] = "48"
        });

        builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        builder.Services.AddValidatorsFromAssemblyContaining<WalkInCustomerRequestValidator>();
        builder.Services.AddPasswordRecovery(builder.Configuration);

        var dbOptions = new DbContextOptionsBuilder<VfmsDbContext>();
        var sqlConnection = Environment.GetEnvironmentVariable("VFMS_TEST_SQL");
        if (string.IsNullOrWhiteSpace(sqlConnection))
        {
            await _connection.OpenAsync();
            _dbOptions = dbOptions.UseSqlite(_connection).Options;
            builder.Services.AddScoped<VfmsDbContext>(_ => new SqliteTestContext(_dbOptions));
        }
        else
        {
            _sqlDatabaseName = "VFMS_CustomerSetupTest_" + Guid.NewGuid().ToString("N");
            var connection = new SqlConnectionStringBuilder(sqlConnection) { InitialCatalog = _sqlDatabaseName };
            _dbOptions = dbOptions.UseSqlServer(connection.ConnectionString).Options;
            builder.Services.AddScoped(_ => new VfmsDbContext(_dbOptions));
        }
        builder.Services.AddSingleton<IPasswordHasher>(_hasher);
        builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
        builder.Services.AddScoped<IAuthRepository, AuthRepository>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IUserAdministrationRepository, UserAdministrationRepository>();
        builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddScoped<ITrainerRepository, TrainerRepository>();
        builder.Services.AddScoped<ITrainerService, TrainerService>();
        builder.Services.AddRateLimiter(options => options.AddPolicy("trainer-public",
            _ => RateLimitPartition.GetNoLimiter("trainer-test")));
        builder.Services.AddSingleton<IEmailService>(_emailService);
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddSingleton(CustomerPasswordSetupSettings.FromConfiguration(SetupUrl, isDevelopment: false));

        var mapsterConfig = new TypeAdapterConfig();
        mapsterConfig.Scan(typeof(UserAdministrationMapping).Assembly);
        builder.Services.AddSingleton(mapsterConfig);
        builder.Services.AddScoped<IMapper, ServiceMapper>();

        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            return next(context);
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapControllers();
        await _app.StartAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        if (_sqlDatabaseName is null)
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        var gym = new Gym { GymName = "UC-06 Test Gym" };
        db.Gyms.Add(gym);
        foreach (var roleName in new[] { "Customer", "Member", "PT", "YogaInstructor", "Staff", "Manager", "Admin" })
            if (!await db.ApplicationRoles.AnyAsync(role => role.RoleName == roleName))
                db.ApplicationRoles.Add(new ApplicationRole { RoleName = roleName });
        await db.SaveChangesAsync();
        _gymId = gym.Id;

        await SeedUserAsync("staff-actor-1", "Front Desk Staff", "staff1@vfms.test", "0900000001", "Staff", AccountStatus.Active);
        await SeedUserAsync("manager-actor-1", "Gym Manager", "manager1@vfms.test", "0900000002", "Manager", AccountStatus.Active);
        await SeedUserAsync("admin-actor-1", "System Admin", "admin1@vfms.test", "0900000003", "Admin", AccountStatus.Active);
        await SeedUserAsync("pt-actor-1", "Personal Trainer", "pt1@vfms.test", "0900000004", "PT", AccountStatus.Active);
        await SeedUserAsync("customer-actor-1", "Existing Customer", "customer1@vfms.test", "0900000005", "Customer", AccountStatus.Active);
        await SeedUserAsync("member-actor-1", "Existing Member", "member1@vfms.test", "0900000006", "Member", AccountStatus.Active);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_app is not null)
                await _app.DisposeAsync();
        }
        finally
        {
            if (_sqlDatabaseName is not null && _dbOptions is not null)
            {
                await using var db = new VfmsDbContext(_dbOptions);
                Assert.StartsWith("VFMS_CustomerSetupTest_", _sqlDatabaseName);
                Assert.Equal(_sqlDatabaseName, db.Database.GetDbConnection().Database);
                await db.Database.EnsureDeletedAsync();
            }
            await _connection.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("staff-actor-1", "Staff", HttpStatusCode.Created)]
    [InlineData("manager-actor-1", "Manager", HttpStatusCode.Created)]
    [InlineData("admin-actor-1", "Admin", HttpStatusCode.Forbidden)]
    [InlineData("pt-actor-1", "PT", HttpStatusCode.Forbidden)]
    [InlineData("customer-actor-1", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("member-actor-1", "Member", HttpStatusCode.Forbidden)]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    public async Task AuthorizationMatrixEnforcesStaffAndManagerOnly(
        string? actorId,
        string? role,
        HttpStatusCode expectedStatus)
    {
        using var client = CreateClient(actorId, role);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var phoneDigits = "09" + RandomNumberGenerator.GetInt32(10000000, 99999999);

        using var response = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Walk In Guest", phoneDigits, $"walkin-{suffix}@example.com"));

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task MaliciousStaffRoleEscalationPayloadAlwaysAssignsCustomerOnly()
    {
        using var client = CreateClient("staff-actor-1", "Staff");
        var payload = new
        {
            fullName = "Escalation Attempt",
            phoneNumber = "0911223344",
            email = "escalation@example.com",
            role = "Manager",
            roleName = "Admin",
            roleId = 7,
            roles = new[] { "Admin", "Manager", "Staff" }
        };

        using var response = await client.PostAsJsonAsync(CreateCustomerRoute, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(new[] { "Customer" }, body.Roles);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var roles = await db.ApplicationUserRoles
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == body.UserId)
            .Select(ur => ur.Role.RoleName)
            .ToListAsync();
        Assert.Equal(new[] { "Customer" }, roles);
    }

    [Fact]
    public async Task Gbr24RejectsDuplicateEmailDuplicatePhoneWithUniqueEmailAndFormattedPhoneVariants()
    {
        using var client = CreateClient("staff-actor-1", "Staff");

        using var initial = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("First Customer", "0912-345-678", "first.customer@example.com"));
        Assert.Equal(HttpStatusCode.Created, initial.StatusCode);

        // Duplicate email (case-insensitive) with unique phone -> 409 AUTH_EMAIL_EXISTS
        using var dupEmail = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Second Customer", "0988776655", "FIRST.CUSTOMER@EXAMPLE.COM"));
        Assert.Equal(HttpStatusCode.Conflict, dupEmail.StatusCode);
        Assert.Contains("AUTH_EMAIL_EXISTS", await dupEmail.Content.ReadAsStringAsync());

        // Duplicate exact phone WITH unique email -> 409 USER_PHONE_EXISTS
        using var dupPhone = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Third Customer", "0912345678", "third.customer@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, dupPhone.StatusCode);
        Assert.Contains("USER_PHONE_EXISTS", await dupPhone.Content.ReadAsStringAsync());

        // Duplicate formatted phone variant WITH unique email -> 409 USER_PHONE_EXISTS
        using var dupPhoneVariant = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Fourth Customer", "(0912) 345 678", "fourth.customer@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, dupPhoneVariant.StatusCode);
        Assert.Contains("USER_PHONE_EXISTS", await dupPhoneVariant.Content.ReadAsStringAsync());

        // Missing email -> 400 BadRequest
        using var missingEmail = await client.PostAsJsonAsync(
            CreateCustomerRoute,
            new { fullName = "No Email Customer", phoneNumber = "0977665544", email = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingEmail.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSamePhoneAllowOnlyOneCreation()
    {
        using var client1 = CreateClient("staff-actor-1", "Staff");
        using var client2 = CreateClient("manager-actor-1", "Manager");

        var task1 = client1.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Concurrent A", "0933445566", "concurrent.a@example.com"));
        var task2 = client2.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Concurrent B", "0933-445-566", "concurrent.b@example.com"));

        var responses = await Task.WhenAll(task1, task2);
        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var count = await db.ApplicationUsers.CountAsync(u => u.PhoneNumber == "0933445566");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EndToEndOnboardingPendingToActiveWith48HourTokenAuditAndLoginGating()
    {
        using var staffClient = CreateClient("staff-actor-1", "Staff");
        using var anonClient = CreateClient(null, null);

        // 1. Staff creates customer
        using var createResponse = await staffClient.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Walk-In Member", "0944-556-677", "walkin.flow@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.True(createResponse.Headers.CacheControl?.NoStore);

        var rawJson = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("generatedPassword", rawJson, StringComparison.OrdinalIgnoreCase);

        var created = JsonSerializer.Deserialize<WalkInCustomerResponse>(rawJson, JsonOptions)!;
        Assert.Equal("PendingVerification", created.AccountStatus);
        Assert.Equal(new[] { "Customer" }, created.Roles);
        Assert.Equal("0944556677", created.PhoneNumber);
        Assert.True(created.SetupEmailSent);
        Assert.Equal(_clock.CurrentUtcNow.UtcDateTime.AddHours(48), created.PasswordSetupExpiresAt);

        // 2. Verify email dispatched with 48-hour setup link and raw token never leaked in response/DB/audit
        var sentEmail = Assert.Single(_emailService.SentMessages, m => m.Recipient == "walkin.flow@example.com");
        Assert.Contains($"{SetupUrl}#token=", sentEmail.Body);
        var rawToken = ExtractTokenFromBody(sentEmail.Body);
        Assert.DoesNotContain(rawToken, rawJson, StringComparison.Ordinal);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .SingleAsync(u => u.UserId == created.UserId);
            Assert.Equal(AccountStatus.PendingVerification, user.AccountStatus);
            Assert.Equal(ComputeSetupHash("VFMS:customer-password-setup:v1:", rawToken), user.PasswordSetupTokenHash);
            Assert.NotEqual(rawToken, user.PasswordSetupTokenHash);
            Assert.Equal(_clock.CurrentUtcNow.UtcDateTime.AddHours(48), user.PasswordSetupExpiresAt);
            Assert.DoesNotContain("@walkin.invalid", user.Email, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "Customer" }, user.UserRoles.Select(ur => ur.Role.RoleName).ToArray());

            var createAudit = await db.AuditLogs.SingleAsync(
                a => a.ActionType == AuditActionType.Create && a.AuditEntity == $"ApplicationUser:{created.UserId}");
            Assert.Equal("staff-actor-1", createAudit.ActorUserId);
            Assert.Contains("CustomerSetupLinkIssued", createAudit.NewValue);
            Assert.DoesNotContain(rawToken, createAudit.NewValue ?? string.Empty);
            Assert.Contains(await db.AuditLogs.Where(a => a.AuditEntity == createAudit.AuditEntity).ToListAsync(),
                a => a.NewValue == "Action=CustomerSetupEmailAccepted");
        }

        // 3. Login denied before password setup
        using var loginBefore = await anonClient.PostAsJsonAsync(
            LoginRoute,
            new LoginRequest(" WALKIN.FLOW@example.com ", "Customer@123"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginBefore.StatusCode);

        // 4. Customer completes password setup
        using var setupResponse = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(rawToken, "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        Assert.True(setupResponse.Headers.CacheControl?.NoStore);

        // 5. Verify DB state: Active, token cleared, password hashed with BCrypt, setup audited safely
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == created.UserId);
            Assert.Equal(AccountStatus.Active, user.AccountStatus);
            Assert.Null(user.PasswordSetupTokenHash);
            Assert.Null(user.PasswordSetupExpiresAt);
            Assert.NotEqual("Customer@123", user.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("Customer@123", user.PasswordHash));

            var setupAudit = await db.AuditLogs.SingleAsync(
                a => a.ActionType == AuditActionType.PasswordChange && a.AuditEntity == $"ApplicationUser:{created.UserId}");
            Assert.Equal(created.UserId, setupAudit.ActorUserId);
            Assert.DoesNotContain("Customer@123", setupAudit.NewValue ?? string.Empty);
            Assert.DoesNotContain(rawToken, setupAudit.NewValue ?? string.Empty);
        }

        // 6. Second use of the same setup token fails
        using var replayResponse = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(rawToken, "Another@123", "Another@123"));
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
        Assert.Contains("AUTH_INVALID_SETUP_TOKEN", await replayResponse.Content.ReadAsStringAsync());

        // 7. Customer can now log in with email and phone
        using var loginByEmail = await anonClient.PostAsJsonAsync(
            LoginRoute,
            new LoginRequest("walkin.flow@example.com", "Customer@123"));
        Assert.Equal(HttpStatusCode.OK, loginByEmail.StatusCode);
        var loginData = await loginByEmail.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.NotNull(loginData);
        Assert.Equal(new[] { "Customer" }, loginData.Roles);

        foreach (var phone in new[] { "0944556677", "0944-556-677", "0944 556 677" })
        {
            using var loginByPhone = await anonClient.PostAsJsonAsync(LoginRoute, new LoginRequest(phone, "Customer@123"));
            Assert.Equal(HttpStatusCode.OK, loginByPhone.StatusCode);
        }
    }

    [Fact]
    public async Task ExpiredMalformedAndNonCustomerSetupTokensAreRejected()
    {
        using var staffClient = CreateClient("staff-actor-1", "Staff");
        using var anonClient = CreateClient(null, null);

        using var createResponse = await staffClient.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Expiring User", "0955667788", "expiring.user@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = (await createResponse.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions))!;
        var sentEmail = _emailService.SentMessages.Last(m => m.Recipient == "expiring.user@example.com");
        var rawToken = ExtractTokenFromBody(sentEmail.Body);

        // Malformed token -> 400
        using var malformed = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest("not-a-valid-hex-token", "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.True(malformed.Headers.CacheControl?.NoStore);

        // Advance clock past 48 hours -> expired token rejected
        _clock.CurrentUtcNow = _clock.CurrentUtcNow.AddHours(48).AddSeconds(1);
        using var expired = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(rawToken, "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains("AUTH_INVALID_SETUP_TOKEN", await expired.Content.ReadAsStringAsync());

        // A Trainer-purpose digest is rejected even on an otherwise eligible Customer row.
        var ptToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await SeedUserAsync("trainer-digest-1", "Pending Customer", "trainer.digest@vfms.test", "0966778899", "Customer",
            AccountStatus.PendingVerification, ComputeSetupHash("VFMS:trainer-setup:v1:", ptToken),
            _clock.CurrentUtcNow.UtcDateTime.AddHours(48));

        using var wrongRoleSetup = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(ptToken, "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongRoleSetup.StatusCode);
        Assert.Contains("AUTH_INVALID_SETUP_TOKEN", await wrongRoleSetup.Content.ReadAsStringAsync());

        // Account still PendingVerification
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stillPending = await db.ApplicationUsers.SingleAsync(u => u.UserId == created.UserId);
        Assert.Equal(AccountStatus.PendingVerification, stillPending.AccountStatus);
    }

    [Fact]
    public async Task ResendSetupLinkInvalidatesOldTokenRejectsActiveAndRecoversFromEmailFailure()
    {
        using var staffClient = CreateClient("staff-actor-1", "Staff");
        using var adminClient = CreateClient("admin-actor-1", "Admin");
        using var anonClient = CreateClient(null, null);

        // 1. SMTP unconfigured -> 503 and no user created
        _emailService.IsConfigured = false;
        using var unconfigured = await staffClient.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Blocked User", "0977889900", "blocked.smtp@example.com"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.False(await db.ApplicationUsers.AnyAsync(user => user.Email == "blocked.smtp@example.com"));
        }
        _emailService.IsConfigured = true;

        // 2. Create customer successfully -> token1 issued
        using var createdResponse = await staffClient.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Resend User", "0977889901", "resend.user@example.com"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions))!;
        var token1 = ExtractTokenFromBody(_emailService.SentMessages.Last(m => m.Recipient == "resend.user@example.com").Body);

        // Admin cannot call resend -> 403
        using var adminResend = await adminClient.PostAsync($"/api/users/{created.UserId}/resend-setup-email", null);
        Assert.Equal(HttpStatusCode.Forbidden, adminResend.StatusCode);

        // 3. Advance time 2 hours and Staff resends -> token2 replaces token1 with fresh 48h expiry
        _clock.CurrentUtcNow = _clock.CurrentUtcNow.AddHours(2);
        using var resendResponse = await staffClient.PostAsync($"/api/users/{created.UserId}/resend-setup-email", null);
        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var resent = (await resendResponse.Content.ReadFromJsonAsync<ResendSetupEmailResponse>(JsonOptions))!;
        Assert.Equal(_clock.CurrentUtcNow.UtcDateTime.AddHours(48), resent.PasswordSetupExpiresAt);
        var token2 = ExtractTokenFromBody(_emailService.SentMessages.Last(m => m.Recipient == "resend.user@example.com").Body);
        Assert.NotEqual(token1, token2);

        // Old token1 is now invalid
        using var oldTokenAttempt = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(token1, "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.BadRequest, oldTokenAttempt.StatusCode);

        // New token2 succeeds and activates the account
        using var newTokenAttempt = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(token2, "Customer@123", "Customer@123"));
        Assert.Equal(HttpStatusCode.OK, newTokenAttempt.StatusCode);

        // Active customer can no longer receive resend onboarding link -> 409
        using var activeResend = await staffClient.PostAsync($"/api/users/{created.UserId}/resend-setup-email", null);
        Assert.Equal(HttpStatusCode.Conflict, activeResend.StatusCode);
        Assert.Contains("USER_SETUP_RESEND_NOT_ELIGIBLE", await activeResend.Content.ReadAsStringAsync());

        // Transient SMTP failure during initial create clears token and allows resend recovery
        _emailService.ThrowOnSend = true;
        using var failedMailCreate = await staffClient.PostAsJsonAsync(
            CreateCustomerRoute,
            new WalkInCustomerRequest("Retry Mail User", "0977889902", "retry.mail@example.com"));
        Assert.Equal(HttpStatusCode.Created, failedMailCreate.StatusCode);
        var failedBody = (await failedMailCreate.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions))!;
        Assert.False(failedBody.SetupEmailSent);
        Assert.Null(failedBody.PasswordSetupExpiresAt);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var pending = await db.ApplicationUsers.SingleAsync(user => user.UserId == failedBody.UserId);
            Assert.Equal(AccountStatus.PendingVerification, pending.AccountStatus);
            Assert.Null(pending.PasswordSetupTokenHash);
            Assert.Null(pending.PasswordSetupExpiresAt);
        }

        _emailService.ThrowOnSend = false;
        using var recoverResend = await staffClient.PostAsync($"/api/users/{failedBody.UserId}/resend-setup-email", null);
        Assert.Equal(HttpStatusCode.OK, recoverResend.StatusCode);
        var recoveredToken = ExtractTokenFromBody(_emailService.SentMessages.Last(m => m.Recipient == "retry.mail@example.com").Body);

        using var recoveredSetup = await anonClient.PostAsJsonAsync(
            SetupPasswordRoute,
            new SetupPasswordRequest(recoveredToken, "Recovered@123", "Recovered@123"));
        Assert.Equal(HttpStatusCode.OK, recoveredSetup.StatusCode);
    }

    [Fact]
    public async Task TrainerCannotConsumeAStillValidCustomerLink()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var anonymous = CreateClient(null, null);
        var (_, token) = await CreatePendingAsync(staff);

        using var wrongEndpoint = await anonymous.PostAsJsonAsync(TrainerSetupRoute, new TrainerSetupRequest(token, "Customer@123"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongEndpoint.StatusCode);
        using var rightEndpoint = await SetupAsync(anonymous, token);
        Assert.Equal(HttpStatusCode.OK, rightEndpoint.StatusCode);
    }

    [Fact]
    public async Task SimultaneousCustomerSetupHasExactlyOneWinner()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var first = CreateClient(null, null);
        using var second = CreateClient(null, null);
        var (created, token) = await CreatePendingAsync(staff);

        var responses = await Task.WhenAll(SetupAsync(first, token), SetupAsync(second, token));
        using var response1 = responses[0];
        using var response2 = responses[1];
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }, responses.Select(r => r.StatusCode).Order());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
            a.AuditEntity == $"ApplicationUser:{created.UserId}" && a.ActionType == AuditActionType.PasswordChange));
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Manager")]
    public async Task RoleAddedAfterInitialSetupReadPreventsWrite(string addedRole)
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var anonymous = CreateClient(null, null);
        var (created, token) = await CreatePendingAsync(staff);
        var hashingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resumeHashing = new ManualResetEventSlim();
        _hasher.BeforeNextHash = () =>
        {
            hashingStarted.SetResult();
            if (!resumeHashing.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The role-change test did not release password hashing.");
        };

        var setup = Task.Run(() => SetupAsync(anonymous, token));
        try
        {
            await hashingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var roleId = await db.ApplicationRoles.Where(role => role.RoleName == addedRole).Select(role => role.Id).SingleAsync();
            db.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = created.UserId, RoleId = roleId });
            await db.SaveChangesAsync();
        }
        finally
        {
            resumeHashing.Set();
        }

        using var result = await setup;
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var resend = await staff.PostAsync(ResendRoute(created.UserId), null);
        Assert.Equal(HttpStatusCode.Conflict, resend.StatusCode);
        Assert.Single(_emailService.SentMessages);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(1, HttpStatusCode.BadRequest)]
    public async Task CustomerSetupExpiryUsesStrictUtcBoundary(int ticks, HttpStatusCode expected)
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var anonymous = CreateClient(null, null);
        var (_, token) = await CreatePendingAsync(staff);
        _clock.CurrentUtcNow = _clock.CurrentUtcNow.AddHours(48).AddTicks(ticks);

        using var result = await SetupAsync(anonymous, token);
        Assert.Equal(expected, result.StatusCode);
    }

    [Fact]
    public async Task TokenThatExpiresDuringPasswordHashingCannotBeConsumed()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var anonymous = CreateClient(null, null);
        var (_, token) = await CreatePendingAsync(staff);
        _clock.CurrentUtcNow = _clock.CurrentUtcNow.AddHours(48).AddTicks(-1);
        _hasher.BeforeNextHash = () => _clock.CurrentUtcNow = _clock.CurrentUtcNow.AddTicks(1);

        using var result = await SetupAsync(anonymous, token);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task FailedResendPreservesPriorCredentialAndRecordsOnlyFailure()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var anonymous = CreateClient(null, null);
        var (created, token) = await CreatePendingAsync(staff);
        _emailService.ThrowOnSend = true;

        using var failed = await staff.PostAsync(ResendRoute(created.UserId), null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Contains("CUSTOMER_SETUP_EMAIL_FAILED", await failed.Content.ReadAsStringAsync());

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var events = await db.AuditLogs.Where(a => a.AuditEntity == $"ApplicationUser:{created.UserId}")
                .Select(a => a.NewValue).ToListAsync();
            Assert.Single(events, value => value == "Action=CustomerSetupEmailAccepted");
            Assert.Single(events, value => value != null && value.Contains("CustomerSetupLinkIssued"));
            Assert.Single(events, value => value == "Action=CustomerSetupEmailFailed");
            Assert.All(events, value => Assert.DoesNotContain(token, value ?? string.Empty, StringComparison.Ordinal));
        }

        using var setup = await SetupAsync(anonymous, token);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
    }

    [Fact]
    public async Task OverlappingResendsRejectSecondRequestAndReturnOneUsableLink()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var manager = CreateClient("manager-actor-1", "Manager");
        using var anonymous = CreateClient(null, null);
        var (created, oldToken) = await CreatePendingAsync(staff);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _emailService.BeforeSend = async cancellationToken =>
        {
            sending.TrySetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        };

        var first = staff.PostAsync(ResendRoute(created.UserId), null);
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var competing = await manager.PostAsync(ResendRoute(created.UserId), null);
            Assert.Equal(HttpStatusCode.Conflict, competing.StatusCode);
            Assert.Contains("CUSTOMER_SETUP_RESEND_BUSY", await competing.Content.ReadAsStringAsync());
        }
        finally
        {
            finish.TrySetResult();
        }

        using var winner = await first;
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Equal(2, _emailService.SentMessages.Count);
        using var old = await SetupAsync(anonymous, oldToken);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var replacement = await SetupAsync(anonymous, ExtractTokenFromBody(_emailService.SentMessages.Last().Body));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
    }

    [Fact]
    public async Task StaffCanDiscoverPendingAccountWithoutOriginalResponseAndResend()
    {
        using var staff = CreateClient("staff-actor-1", "Staff");
        using var manager = CreateClient("manager-actor-1", "Manager");
        using var admin = CreateClient("admin-actor-1", "Admin");
        _emailService.ThrowOnSend = true;
        using var ignoredCreateResponse = await staff.PostAsJsonAsync(CreateCustomerRoute,
            new WalkInCustomerRequest("Recoverable Customer", "0922-100-200", "recover@example.com"));
        Assert.Equal(HttpStatusCode.Created, ignoredCreateResponse.StatusCode);

        using var found = await manager.PostAsJsonAsync(FindPendingRoute,
            new FindPendingCustomerRequest(" RECOVER@example.com ", "(0922) 100 200"));
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.True(found.Headers.CacheControl?.NoStore);
        var recovered = (await found.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions))!;
        Assert.Equal("PendingVerification", recovered.AccountStatus);
        Assert.False(recovered.SetupEmailSent);
        Assert.Null(recovered.PasswordSetupExpiresAt);

        using var mismatch = await manager.PostAsJsonAsync(FindPendingRoute,
            new FindPendingCustomerRequest("recover@example.com", "0900000001"));
        using var staffTarget = await manager.PostAsJsonAsync(FindPendingRoute, new FindPendingCustomerRequest("staff1@vfms.test"));
        using var unauthorized = await admin.PostAsJsonAsync(FindPendingRoute, new FindPendingCustomerRequest("recover@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, staffTarget.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);

        _emailService.ThrowOnSend = false;
        using var resent = await manager.PostAsync(ResendRoute(recovered.UserId), null);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Single(_emailService.SentMessages);
    }

    [Fact]
    public async Task SetupThrottlingDoesNotUsePasswordRecoveryBudget()
    {
        using var anonymous = CreateClient(null, null);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var malformed = await SetupAsync(anonymous, "invalid");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        using var rateLimited = await SetupAsync(anonymous, "invalid");
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
        Assert.True(rateLimited.Headers.CacheControl?.NoStore);
        Assert.Contains("AUTH_SETUP_RATE_LIMITED", await rateLimited.Content.ReadAsStringAsync());

        using var reset = await anonymous.PostAsJsonAsync(
            "/api/auth/forgot-password", new ForgotPasswordRequest("unregistered@example.test"));
        Assert.Equal(HttpStatusCode.Accepted, reset.StatusCode);
    }

    private async Task<(WalkInCustomerResponse User, string Token)> CreatePendingAsync(HttpClient staff)
    {
        using var response = await staff.PostAsJsonAsync(CreateCustomerRoute,
            new WalkInCustomerRequest("Pending Customer", "0922100300", "pending@example.com"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<WalkInCustomerResponse>(JsonOptions))!,
            ExtractTokenFromBody(_emailService.SentMessages.Last().Body));
    }

    private static Task<HttpResponseMessage> SetupAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync(SetupPasswordRoute, new SetupPasswordRequest(token, "Customer@123", "Customer@123"));

    private static string ResendRoute(string userId) => $"/api/users/{userId}/resend-setup-email";

    private HttpClient CreateClient(string? userId, string? role)
    {
        var client = _app.GetTestClient();
        if (!string.IsNullOrWhiteSpace(userId))
            client.DefaultRequestHeaders.Add(UserIdHeader, userId);
        if (!string.IsNullOrWhiteSpace(role))
            client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    private async Task SeedUserAsync(
        string userId,
        string fullName,
        string email,
        string phoneNumber,
        string roleName,
        AccountStatus status,
        string? setupTokenHash = null,
        DateTime? setupExpiresAt = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var role = await db.ApplicationRoles.SingleAsync(r => r.RoleName == roleName);
        db.ApplicationUsers.Add(new ApplicationUser
        {
            UserId = userId,
            GymId = _gymId,
            FullName = fullName,
            Email = email,
            PhoneNumber = phoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("SeedPass@123", workFactor: 4),
            AccountStatus = status,
            PasswordSetupTokenHash = setupTokenHash,
            PasswordSetupExpiresAt = setupExpiresAt
        });
        db.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private static string ExtractTokenFromBody(string body)
    {
        const string marker = "#token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Setup email body must contain #token= fragment.");
        return body.Substring(start + marker.Length, 64);
    }

    private static string ComputeSetupHash(string prefix, string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix + raw)));

    private sealed class CapturingEmailService : IEmailService
    {
        public bool IsConfigured { get; set; } = true;
        public bool ThrowOnSend { get; set; }
        public Func<CancellationToken, Task>? BeforeSend { get; set; }
        public ConcurrentQueue<(string Recipient, string Subject, string Body)> SentMessages { get; } = new();

        public async Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default)
        {
            if (BeforeSend is not null)
                await BeforeSend(cancellationToken);
            if (ThrowOnSend)
                throw new InvalidOperationException("Simulated SMTP failure.");

            SentMessages.Enqueue((recipient, subject, textBody));
        }
    }

    private sealed class ControllablePasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher _hasher = new();
        public Action? BeforeNextHash;

        public string Hash(string password)
        {
            Interlocked.Exchange(ref BeforeNextHash, null)?.Invoke();
            return _hasher.Hash(password);
        }

        public bool Verify(string password, string hash) => _hasher.Verify(password, hash);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset CurrentUtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => CurrentUtcNow;
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

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new("sub", userId)
            };

            if (Request.Headers.TryGetValue(RoleHeader, out var roleValues))
            {
                foreach (var role in roleValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
