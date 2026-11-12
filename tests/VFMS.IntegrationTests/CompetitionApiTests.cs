using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VFMS.API.Controllers;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Competitions;
using VFMS.Contracts.Competitions.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>
/// Module 6 – Competitions (UC-56..UC-62) against a real MySQL-backed database.
/// The API is hosted in-process with the same controller and validation pipeline.
/// </summary>
public sealed class CompetitionApiTests
{
    private const string ConnectionString =
        "Server=localhost;Port=3306;Database=VfmsDb;User Id=vfms;Password=Vfms@2026;TreatTinyAsBoolean=true;AllowUserVariables=true;CharSet=utf8mb4";

    /// <summary>Unique per test run so the shared MySQL database never collides on emails or names.</summary>
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task MemberCanRegisterOnlyOnceAndOnlyInsideTheRegistrationWindow()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager One", "manager.one@example.com", "Manager");
        var member = await SeedUserAsync(app, "Member One", "member.one@example.com", "Member");

        // A Draft competition does not accept registrations.
        var draft = await CreateCompetitionAsync(client, manager, "Draft Cup",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftId = await ReadIdAsync(draft);

        var rejected = await PostAsync(client, $"/api/competitions/{draftId}/register", member);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // Publish it, then register.
        var published = await PostAsync(client, $"/api/competitions/{draftId}/publish", manager);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var registered = await PostAsync(client, $"/api/competitions/{draftId}/register", member);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // A second registration for the same competition is rejected.
        var again = await PostAsync(client, $"/api/competitions/{draftId}/register", member);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // The participant appears in the member's own history (UC-58).
        var history = await GetAsync(client, "/api/competitions/my/history", member);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(history)).GetProperty("totalCount").GetInt32());

        // The staff sees the same participant in the participant list (UC-60).
        var staff = await SeedUserAsync(app, "Staff One", "staff.one@example.com", "Staff");
        var participants = await GetAsync(client, $"/api/competitions/participants?competitionId={draftId}", staff);
        Assert.Equal(HttpStatusCode.OK, participants.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(participants)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task RegistrationIsRejectedWhenTheCompetitionIsFull()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager Cap", "manager.cap@example.com", "Manager");
        var first = await SeedUserAsync(app, "Member Cap A", "cap.a@example.com", "Member");
        var second = await SeedUserAsync(app, "Member Cap B", "cap.b@example.com", "Member");

        var created = await CreateCompetitionAsync(client, manager, "Capacity Cup",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3), maxParticipants: 1);
        var id = await ReadIdAsync(created);
        await PostAsync(client, $"/api/competitions/{id}/publish", manager);

        var firstRegistration = await PostAsync(client, $"/api/competitions/{id}/register", first);
        Assert.Equal(HttpStatusCode.Created, firstRegistration.StatusCode);

        var secondRegistration = await PostAsync(client, $"/api/competitions/{id}/register", second);
        Assert.Equal(HttpStatusCode.Conflict, secondRegistration.StatusCode);
    }

    [Fact]
    public async Task StaffRecordsResultAndManagerConfirmsItAndGrantsThePrizeVoucher()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager Result", "manager.result@example.com", "Manager");
        var staff = await SeedUserAsync(app, "Staff Result", "staff.result@example.com", "Staff");
        var member = await SeedUserAsync(app, "Member Result", "member.result@example.com", "Member");
        var voucherId = await SeedVoucherAsync(app, "PRIZE2026", manager.UserId);

        var created = await CreateCompetitionAsync(client, manager, "Prize Cup",
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3), prizes:
            [new CompetitionPrizeRequest("Champion", 1, null, voucherId)]);
        var competitionId = await ReadIdAsync(created);
        await PostAsync(client, $"/api/competitions/{competitionId}/publish", manager);

        var registered = await PostAsync(client, $"/api/competitions/{competitionId}/register", member);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var joinId = await ReadIdAsync(registered);

        // The competition must be Completed before a result can be confirmed (JOB-05 advances the status).
        await SetCompetitionStatusAsync(app, competitionId, CompetitionStatus.Completed);

        // Staff records the preliminary score and rank (UC-61).
        var recorded = await PostJsonAsync(client, $"/api/competitions/participants/{joinId}/result",
            new RecordCompetitionResultRequest(95.5m, 1, "Great form"), staff);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        // The Manager confirms the result and grants the prize voucher (UC-62).
        var confirmed = await PostJsonAsync(client, "/api/competitions/results/confirm",
            new ConfirmCompetitionResultRequest(joinId, 95.5m, 1, null, true), manager);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedBody = await ReadJsonAsync(confirmed);
        Assert.Equal("Completed", confirmedBody.GetProperty("status").GetString());
        Assert.Equal(1, confirmedBody.GetProperty("rank").GetInt32());
        Assert.Equal("Great form", confirmedBody.GetProperty("note").GetString());

        // The winner got a notification with the voucher code.
        var notifications = await app.Services.GetRequiredService<VfmsDbContext>()
            .Notifications.Where(n => n.RecipientUserId == member.UserId).ToListAsync();
        Assert.Contains(notifications, n => n.NotificationType == nameof(NotificationType.CompetitionResult)
            && n.Message.Contains($"PRIZE2026{RunId}", StringComparison.Ordinal));

        // The leaderboard shows the confirmed participant.
        var leaderboard = await GetAsync(client, $"/api/competitions/{competitionId}/leaderboard", member);
        Assert.Equal(HttpStatusCode.OK, leaderboard.StatusCode);
        var entries = JsonSerializer.Deserialize<List<CompetitionLeaderboardEntry>>(
            await leaderboard.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(entries);
        Assert.Single(entries!);
        Assert.Equal("Champion", entries![0].PrizeName);
        Assert.Equal($"PRIZE2026{RunId}", entries[0].VoucherCode);
        Assert.Equal("Great form", entries[0].Note);
    }

    [Fact]
    public async Task ManagerCannotConfirmAResultWhileTheCompetitionIsStillRunning()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager Running", "manager.running@example.com", "Manager");
        var member = await SeedUserAsync(app, "Member Running", "member.running@example.com", "Member");

        var created = await CreateCompetitionAsync(client, manager, "Running Cup",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3));
        var competitionId = await ReadIdAsync(created);
        await PostAsync(client, $"/api/competitions/{competitionId}/publish", manager);

        var registered = await PostAsync(client, $"/api/competitions/{competitionId}/register", member);
        var joinId = await ReadIdAsync(registered);

        var confirmed = await PostJsonAsync(client, "/api/competitions/results/confirm",
            new ConfirmCompetitionResultRequest(joinId, 10m, 1), manager);
        Assert.Equal(HttpStatusCode.Conflict, confirmed.StatusCode);
    }

    [Fact]
    public async Task DisqualifiedParticipantDoesNotCountTowardsTheCapacity()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager Disq", "manager.disq@example.com", "Manager");
        var staff = await SeedUserAsync(app, "Staff Disq", "staff.disq@example.com", "Staff");
        var first = await SeedUserAsync(app, "Member Disq A", "disq.a@example.com", "Member");
        var second = await SeedUserAsync(app, "Member Disq B", "disq.b@example.com", "Member");

        var created = await CreateCompetitionAsync(client, manager, "Disqualify Cup",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3), maxParticipants: 1);
        var competitionId = await ReadIdAsync(created);
        await PostAsync(client, $"/api/competitions/{competitionId}/publish", manager);

        var firstRegistration = await PostAsync(client, $"/api/competitions/{competitionId}/register", first);
        var joinId = await ReadIdAsync(firstRegistration);

        // The full competition blocks the second member.
        var blocked = await PostAsync(client, $"/api/competitions/{competitionId}/register", second);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Disqualifying the first member frees the slot.
        var disqualified = await PostAsync(client, $"/api/competitions/participants/{joinId}/disqualify", staff);
        Assert.Equal(HttpStatusCode.OK, disqualified.StatusCode);

        var secondRegistration = await PostAsync(client, $"/api/competitions/{competitionId}/register", second);
        Assert.Equal(HttpStatusCode.Created, secondRegistration.StatusCode);
    }

    [Fact]
    public async Task ManagerCannotPublishACompetitionTwice()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var manager = await SeedUserAsync(app, "Manager Twice", "manager.twice@example.com", "Manager");

        var created = await CreateCompetitionAsync(client, manager, "Twice Cup",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3));
        var competitionId = await ReadIdAsync(created);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/api/competitions/{competitionId}/publish", manager)).StatusCode);
        var second = await PostAsync(client, $"/api/competitions/{competitionId}/publish", manager);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task MemberCannotManageCompetitions()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await SeedGymAsync(app);
        var member = await SeedUserAsync(app, "Member No Rights", "member.norights@example.com", "Member");

        var created = await PostJsonAsync(client, "/api/competitions",
            new CompetitionRequest("Forbidden Cup", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3)), member);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<VfmsDbContext>(options => options.UseMySql(
            ConnectionString, new MySqlServerVersion(new Version(8, 0, 39))));
        builder.Services.AddControllers().AddApplicationPart(typeof(CompetitionsController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<CompetitionRequestValidator>();
        builder.Services.AddScoped<ICompetitionRepository, CompetitionRepository>();
        builder.Services.AddScoped<ICompetitionService, CompetitionService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(FakeToken.Key))
            };
        });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string url, ApplicationUser user) =>
        SendAsync(client, HttpMethod.Get, url, user, null);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, ApplicationUser user) =>
        SendAsync(client, HttpMethod.Post, url, user, null);

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body, ApplicationUser user) =>
        SendAsync(client, HttpMethod.Post, url, user, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, ApplicationUser user, object? body)
    {
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FakeToken.For(user));
        if (body is not null)
            message.Content = JsonContent.Create(body);
        return await client.SendAsync(message);
    }

    private static async Task<Gym> SeedGymAsync(WebApplication app)
    {
        var context = app.Services.GetRequiredService<VfmsDbContext>();
        if (await context.Gyms.AnyAsync()) return await context.Gyms.FirstAsync();
        var gym = new Gym { GymName = "Test Gym", OpenTime = new TimeOnly(5, 0), CloseTime = new TimeOnly(23, 0) };
        context.Gyms.Add(gym);
        await context.SaveChangesAsync();
        return gym;
    }

    private static async Task<ApplicationUser> SeedUserAsync(WebApplication app, string fullName, string email, string role)
    {
        var context = app.Services.GetRequiredService<VfmsDbContext>();
        var gymId = (await context.Gyms.FirstAsync()).Id;
        var uniqueEmail = $"{email.Split('@')[0]}.{RunId}@{email.Split('@')[1]}";
        var user = new ApplicationUser
        {
            UserId = Guid.NewGuid().ToString(),
            GymId = gymId,
            FullName = fullName,
            Email = uniqueEmail,
            PhoneNumber = "0900000000",
            PasswordHash = "hash",
            AccountStatus = AccountStatus.Active
        };
        var roleEntity = await context.ApplicationRoles.FirstAsync(r => r.RoleName == role);
        context.ApplicationUsers.Add(user);
        await context.SaveChangesAsync();
        context.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = user.UserId, RoleId = roleEntity.Id });
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<int> SeedVoucherAsync(WebApplication app, string code, string createdByUserId)
    {
        var context = app.Services.GetRequiredService<VfmsDbContext>();
        var voucher = new Voucher
        {
            Code = $"{code}{RunId}",
            Name = "Prize voucher",
            DiscountType = DiscountType.FIXED,
            DiscountValue = 100_000,
            StartAt = DateTime.UtcNow.AddDays(-30),
            EndAt = DateTime.UtcNow.AddDays(30),
            VoucherStatus = VoucherStatus.Active,
            CreatedByUserId = createdByUserId
        };
        context.Vouchers.Add(voucher);
        await context.SaveChangesAsync();
        return voucher.Id;
    }

    private static async Task SetCompetitionStatusAsync(WebApplication app, int competitionId, CompetitionStatus status)
    {
        var context = app.Services.GetRequiredService<VfmsDbContext>();
        var competition = await context.Competitions.FirstAsync(c => c.Id == competitionId);
        competition.Status = status;
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> CreateCompetitionAsync(
        HttpClient client, ApplicationUser manager, string name,
        DateTime registrationStart, DateTime registrationEnd, DateTime start, DateTime end,
        int? maxParticipants = null, IReadOnlyCollection<CompetitionPrizeRequest>? prizes = null)
    {
        var request = new CompetitionRequest($"{name} {RunId}", registrationStart, registrationEnd, start, end,
            Description: "Test competition", MaxParticipants: maxParticipants, Prizes: prizes);
        return await PostJsonAsync(client, "/api/competitions", request, manager);
    }

    private static async Task<int> ReadIdAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}

/// <summary>Builds an unsigned test token that carries the user id and the role name.</summary>
internal static class FakeToken
{
    public const string Key = "VFMS-Test-Signing-Key-For-Integration-Tests-2026";

    public static string For(ApplicationUser user)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId),
            new("sub", user.UserId),
            new("email", user.Email),
            new("name", user.FullName)
        };
        claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleOf(user)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            claims: claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: credentials);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string RoleOf(ApplicationUser user)
    {
        var email = user.Email.ToLowerInvariant();
        if (email.StartsWith("manager", StringComparison.Ordinal)) return "Manager";
        if (email.StartsWith("staff", StringComparison.Ordinal)) return "Staff";
        return "Member";
    }
}
