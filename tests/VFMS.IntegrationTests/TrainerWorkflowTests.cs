using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class SqlTrainerFactAttribute : FactAttribute
{
    public SqlTrainerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VFMS_TEST_SQL")))
            Skip = "Set VFMS_TEST_SQL for isolated SQL Server integration tests.";
    }
}

internal sealed class TrainerTestDatabase : IAsyncDisposable
{
    private readonly string databaseName = "VFMS_TrainerTest_" + Guid.NewGuid().ToString("N");
    private readonly string files = Path.Combine(Path.GetTempPath(), "VFMS_TrainerTest_" + Guid.NewGuid().ToString("N"));
    public VfmsDbContext Db { get; }
    public EphemeralDataProtectionProvider Protection { get; } = new();
    public IConfiguration Configuration { get; }
    public TrainerService Service { get; }
    public TrainerTestDatabase()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VFMS_TEST_SQL"))
        { InitialCatalog = databaseName, ConnectTimeout = 10 };
        Db = new(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlServer(connection.ConnectionString).Options);
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TrainerFiles:Root"] = files,
            ["JwtSettings:Secret"] = "Synthetic-test-signing-key-at-least-32-bytes-long",
            ["JwtSettings:Issuer"] = "vfms-tests", ["JwtSettings:Audience"] = "vfms-tests"
        }).Build();
        Service = new(new TrainerStore(Db, Protection, Configuration), new PasswordHasher());
    }
    public async Task Initialize()
    {
        await Db.Database.MigrateAsync();
        var gym = new Gym { GymName = "Synthetic test gym" };
        Db.Add(gym); await Db.SaveChangesAsync();
        var manager = new ApplicationUser { UserId = "manager", GymId = gym.Id, FullName = "Test Manager", Email = "manager@example.test", PhoneNumber = "0900000099", AccountStatus = AccountStatus.Active };
        var role = new ApplicationRole { RoleName = "Manager" };
        Db.AddRange(manager, role, new ApplicationRole { RoleName = "PT" }); await Db.SaveChangesAsync();
        Db.Add(new ApplicationUserRole { UserId = manager.UserId, RoleId = role.Id }); await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }
    public static T Success<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.ErrorMessage}"); return result.Data!;
    }
    public static SubmitTrainerApplicationRequest Request => new("Test Trainer", "trainer@example.test", "0900000010", "000000000010", new(2000, 1, 1), "Other", "Strength", 3, "Strength coach", true, [new("proof.pdf", "%PDF-1.7 synthetic"u8.ToArray())]);
    public async Task<TrainerApplication> Hire()
    {
        Success(await Service.Submit(Request, null, default));
        var app = await Db.TrainerApplications.SingleAsync();
        Success(await Service.Review(app.Id, new("Interview", "Interview completed"), "manager", default));
        Success(await Service.Review(app.Id, new("Hire", "Verified", "Junior", "Tier1", null, true), "manager", default));
        Db.ChangeTracker.Clear();
        return await Db.TrainerApplications.SingleAsync();
    }
    public async Task<string> SetupToken(int applicationId)
    {
        var json = await Db.TrainerApplications.Where(x => x.Id == applicationId).Select(x => x.EmailQueueJson).SingleAsync();
        var mail = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(json)!.Single(x => x.Subject == "Set up your trainer account" && x.SentAt == null);
        var body = Protection.CreateProtector("VFMS.TrainerEmail.v1").Unprotect(mail.ProtectedBody);
        Assert.StartsWith("SETUP:", body); return body[6..];
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            Assert.StartsWith("VFMS_TrainerTest_", databaseName);
            Assert.Equal(databaseName, Db.Database.GetDbConnection().Database);
            await Db.Database.EnsureDeletedAsync();
        }
        finally
        {
            await Db.DisposeAsync();
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(files)));
            Assert.StartsWith("VFMS_TrainerTest_", Path.GetFileName(files));
            if (Directory.Exists(files)) Directory.Delete(files, true);
        }
    }
}

public class TrainerWorkflowTests
{
    [SqlTrainerFact]
    public async Task Verified_existing_account_can_update_with_current_email()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service; var request = TrainerTestDatabase.Request;
        var customerRole = new ApplicationRole { RoleName = "Customer" };
        var customer = new ApplicationUser
        {
            UserId = "existing-customer", GymId = await db.Gyms.Select(x => x.Id).SingleAsync(),
            FullName = "Existing Customer", Email = "current@example.test", PhoneNumber = request.PhoneNumber,
            PasswordHash = new PasswordHasher().Hash("OriginalPassword1!"), AccountStatus = AccountStatus.Active
        };
        db.AddRange(customerRole, customer); await db.SaveChangesAsync();
        db.Add(new ApplicationUserRole { UserId = customer.UserId, RoleId = customerRole.Id }); await db.SaveChangesAsync();
        var passwordBefore = customer.PasswordHash;
        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        var original = await db.TrainerApplications.SingleAsync();
        TrainerTestDatabase.Success(await service.Review(original.Id, new("Interview", "Identity checked"), "manager", default));
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(original.Id, new("Hire", "Unselected account", "Senior", "Tier2", null, true), "manager", default)).ErrorCode);
        TrainerTestDatabase.Success(await service.Review(original.Id, new("Hire", "Verified matching phone", "Senior", "Tier2", customer.UserId, true), "manager", default));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
        Assert.Equal(2, await db.ApplicationUserRoles.CountAsync(x => x.UserId == customer.UserId));
        var update = request with { ApplicationType = "ProfileUpdate", Email = customer.Email, Specialty = "Yoga" };
        TrainerTestDatabase.Success(await service.Submit(update, customer.UserId, default));
        var firstUpdate = await db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate");
        TrainerTestDatabase.Success(await service.Review(firstUpdate.Id, new("Reject", "Resubmit certificate"), "manager", default));
        TrainerTestDatabase.Success(await service.Submit(update, customer.UserId, default));
        var unchangedAccount = await db.ApplicationUsers.SingleAsync(x => x.UserId == customer.UserId);
        Assert.Equal(passwordBefore, unchangedAccount.PasswordHash); Assert.Equal(customer.Email, unchangedAccount.Email);
        Assert.Null(unchangedAccount.PasswordSetupTokenHash); Assert.Equal(AccountStatus.Active, unchangedAccount.AccountStatus);
        Assert.Equal(2, await db.ApplicationUserRoles.CountAsync(x => x.UserId == customer.UserId));
    }

    [SqlTrainerFact]
    public async Task Recruitment_setup_reissue_and_real_auth_rotation_preserve_single_use_tokens()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service;
        var receipt = TrainerTestDatabase.Success(await service.Submit(TrainerTestDatabase.Request, null, default));
        Assert.Equal("Pending", receipt.Status);
        Assert.Equal(1, await db.ApplicationUsers.CountAsync());
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(TrainerTestDatabase.Request, null, default)).ErrorCode);
        var app = await db.TrainerApplications.SingleAsync();
        var receiptEmail = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(app.EmailQueueJson)!.Single(x => x.Subject == "Trainer application received");
        var receiptBody = fixture.Protection.CreateProtector("VFMS.TrainerEmail.v1").Unprotect(receiptEmail.ProtectedBody);
        Assert.DoesNotContain("Pending Review", receiptBody); Assert.Contains("Pending.", receiptBody);
        var file = JsonSerializer.Deserialize<List<TrainerStoredFile>>(app.CertificatesJson)!.Single();
        Assert.Equal(404, (await service.ApplicationDocument(app.Id, file.Id, null, default)).StatusCode);
        Assert.Equal(TrainerTestDatabase.Request.Certificates[0].Content, TrainerTestDatabase.Success(await service.ApplicationDocument(app.Id, file.Id, "manager", default)).Content);
        Assert.Equal("TRAINER_INTERVIEW_REQUIRED", (await service.Review(app.Id, new("Hire", "Verified", "Junior", "Tier1", null, true), "manager", default)).ErrorCode);
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Interview", "Interview completed"), "manager", default));
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Hire", "Verified", "Junior", "Tier1", null, true), "manager", default));
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Hire", "Retry", "Junior", "Tier1", null, true), "manager", default));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync()); Assert.Equal(1, await db.TrainerProfiles.CountAsync());
        Assert.Empty(TrainerTestDatabase.Success(await service.Search(new(), default)).Items);
        var initialUser = await db.ApplicationUsers.SingleAsync(x => x.Email == TrainerTestDatabase.Request.Email);
        Assert.InRange(initialUser.PasswordSetupExpiresAt!.Value, DateTime.UtcNow.AddHours(47), DateTime.UtcNow.AddHours(49));
        var originalToken = await fixture.SetupToken(app.Id);
        TrainerTestDatabase.Success(await service.ResendSetup(app.Id, "manager", default));
        var replacementToken = await fixture.SetupToken(app.Id); Assert.NotEqual(originalToken, replacementToken);
        Assert.Equal("TRAINER_SETUP_INVALID", (await service.Setup(new(originalToken, "TestPassword1!"), default)).ErrorCode);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Email == TrainerTestDatabase.Request.Email);
        Assert.NotNull(user.PasswordSetupTokenHash); Assert.NotEqual(replacementToken, user.PasswordSetupTokenHash);
        Assert.InRange(user.PasswordSetupExpiresAt!.Value, DateTime.UtcNow.AddHours(47), DateTime.UtcNow.AddHours(49));
        TrainerTestDatabase.Success(await service.Setup(new(replacementToken, "TestPassword1!"), default));
        Assert.False((await service.Setup(new(replacementToken, "OtherPassword1!"), default)).IsSuccess);
        db.ChangeTracker.Clear(); user = await db.ApplicationUsers.SingleAsync(x => x.UserId == user.UserId);
        Assert.Null(user.PasswordSetupTokenHash); Assert.Null(user.PasswordSetupExpiresAt);
        Assert.True(new PasswordHasher().Verify("TestPassword1!", user.PasswordHash));
        Assert.Single(TrainerTestDatabase.Success(await service.Search(new(), default)).Items);
        // Existing authentication must ignore the new onboarding fields for active accounts.
        user.PasswordSetupTokenHash = new string('A', 64); user.PasswordSetupExpiresAt = DateTime.UtcNow.AddHours(1); await db.SaveChangesAsync();
        var auth = new AuthService(new AuthRepository(db), new PasswordHasher(), new JwtTokenService(fixture.Configuration, db));
        Assert.Equal("AUTH_INVALID_CREDENTIALS", (await auth.LoginAsync(new(user.Email, "WrongPassword1!"), "127.0.0.1")).ErrorCode);
        var login = TrainerTestDatabase.Success(await auth.LoginAsync(new(user.Email, "TestPassword1!", true), "127.0.0.1"));
        Assert.Contains("PT", login.Roles); Assert.False(string.IsNullOrEmpty(login.AccessToken));
        var rotated = TrainerTestDatabase.Success(await auth.RefreshTokenAsync(new(login.RefreshToken), "127.0.0.1"));
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
        Assert.Equal("AUTH_INVALID_REFRESH_TOKEN", (await auth.RefreshTokenAsync(new(login.RefreshToken), null)).ErrorCode);
        TrainerTestDatabase.Success(await auth.LogoutAsync(new(rotated.RefreshToken)));
        Assert.Equal("AUTH_INVALID_REFRESH_TOKEN", (await auth.RefreshTokenAsync(new(rotated.RefreshToken), null)).ErrorCode);
        db.ChangeTracker.Clear();
        Assert.All(await db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));
        Assert.NotNull((await db.ApplicationUsers.SingleAsync(x => x.UserId == user.UserId)).LastLoginAt);
    }

    [SqlTrainerFact]
    public async Task Profile_update_keeps_public_profile_until_approval_and_never_changes_accounts_or_roles()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service; var app = await fixture.Hire();
        var reviewed = TrainerTestDatabase.Success(await service.Application(app.Id, "manager", default));
        Assert.Equal("Interview completed", reviewed.InterviewNote);
        Assert.NotEqual(reviewed.InterviewNote, reviewed.ReviewNote);
        TrainerTestDatabase.Success(await service.Setup(new(await fixture.SetupToken(app.Id), "TestPassword1!"), default));
        var profile = await db.TrainerProfiles.SingleAsync(); var userId = profile.PTUserId;
        var userBefore = JsonSerializer.Serialize(await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.UserId).Select(x => new { x.UserId, x.FullName, x.Email, x.PhoneNumber, x.PasswordHash, x.AccountStatus, x.PasswordSetupTokenHash, x.PasswordSetupExpiresAt }).ToListAsync());
        var rolesBefore = await db.ApplicationUserRoles.OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.RoleId }).ToListAsync();
        var oldDetail = TrainerTestDatabase.Success(await service.Public(profile.Id, default));
        var oldFile = oldDetail.Certificates.Single();
        var update = TrainerTestDatabase.Request with { ApplicationType = "ProfileUpdate", Specialty = "Yoga", Bio = "Requested biography", Certificates = [new("yoga.pdf", "%PDF-1.7 yoga"u8.ToArray())] };
        Assert.Equal(403, (await service.Submit(update, null, default)).StatusCode);
        TrainerTestDatabase.Success(await service.Submit(update, userId, default));
        Assert.Equal("TRAINER_CHANGE_PENDING", (await service.Submit(update, userId, default)).ErrorCode);
        var pending = await db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate");
        var pendingFile = JsonSerializer.Deserialize<List<TrainerStoredFile>>(pending.CertificatesJson)!.Single();
        var stillPublic = TrainerTestDatabase.Success(await service.Public(profile.Id, default));
        Assert.Equal("Strength", stillPublic.Profile.Specialty); Assert.Equal(oldFile.Id, stillPublic.Certificates.Single().Id);
        Assert.Single(TrainerTestDatabase.Success(await service.Search(new(Specialty: "Strength"), default)).Items);
        Assert.Equal(404, (await service.Document(profile.Id, pendingFile.Id, null, default)).StatusCode);
        Assert.Equal(404, (await service.ApplicationDocument(pending.Id, pendingFile.Id, null, default)).StatusCode);
        TrainerTestDatabase.Success(await service.ApplicationDocument(pending.Id, pendingFile.Id, userId, default));
        Assert.Equal(403, (await service.Review(pending.Id, new("Approve", "Self approval"), userId, default)).StatusCode);
        TrainerTestDatabase.Success(await service.Review(pending.Id, new("Approve", "Verified credentials"), "manager", default));
        var approved = TrainerTestDatabase.Success(await service.Public(profile.Id, default));
        Assert.Equal("Yoga", approved.Profile.Specialty);
        Assert.Contains(approved.Certificates, file => file.Id == pendingFile.Id);
        Assert.Contains(approved.Certificates, file => file.Id == oldFile.Id);
        Assert.Equal(oldDetail.Profile.Bio, approved.Profile.Bio);
        Assert.Equal(update.Certificates[0].Content, TrainerTestDatabase.Success(await service.Document(profile.Id, pendingFile.Id, null, default)).Content);
        TrainerTestDatabase.Success(await service.Document(profile.Id, oldFile.Id, null, default));
        Assert.Equal(404, (await service.Document(profile.Id + 1000, pendingFile.Id, null, default)).StatusCode);
        Assert.False((await service.Review(pending.Id, new("Approve", "Repeat"), "manager", default)).IsSuccess);
        TrainerTestDatabase.Success(await service.Submit(update with { Specialty = "Pilates" }, userId, default));
        var rejected = await db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate" && x.ApplicationStatus == TrainerApplicationStatus.Pending);
        var profileBeforeReject = await db.TrainerProfiles.AsNoTracking().Select(x => new { x.Specialty, x.CertificatesJson, x.Bio, x.AvatarJson, x.Level, x.CommissionTier }).SingleAsync();
        TrainerTestDatabase.Success(await service.Review(rejected.Id, new("Reject", "Certificate not verified"), "manager", default));
        db.ChangeTracker.Clear();
        Assert.Equal(profileBeforeReject, await db.TrainerProfiles.AsNoTracking().Select(x => new { x.Specialty, x.CertificatesJson, x.Bio, x.AvatarJson, x.Level, x.CommissionTier }).SingleAsync());
        Assert.Equal(2, await db.TrainerApplications.CountAsync(x => x.ApplicantUserId == userId));
        Assert.Equal(TrainerApplicationStatus.Rejected, (await db.TrainerApplications.SingleAsync(x => x.Id == rejected.Id)).ApplicationStatus);

        var replacement = update with
        {
            Certificates = [new("replacement.pdf", "%PDF-1.7 replacement"u8.ToArray(), oldFile.Id)]
        };
        TrainerTestDatabase.Success(await service.Submit(replacement, userId, default));
        var edit = await db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate" &&
            x.ApplicationStatus == TrainerApplicationStatus.Pending);
        var history = TrainerTestDatabase.Success(await service.Application(edit.Id, "manager", default)).SubmissionHistory!;
        Assert.Equal(new[] { edit.Id, rejected.Id, pending.Id }, history.Select(x => x.Id));
        TrainerTestDatabase.Success(await service.Review(edit.Id, new("Approve", "Verified replacement"), "manager", default));
        var afterEdit = TrainerTestDatabase.Success(await service.Public(profile.Id, default));
        Assert.DoesNotContain(afterEdit.Certificates, file => file.Id == oldFile.Id);
        Assert.Contains(afterEdit.Certificates, file => file.Id == pendingFile.Id);
        Assert.Contains(afterEdit.Certificates, file => file.FileName == "replacement.pdf");
        Assert.Equal(userBefore, JsonSerializer.Serialize(await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.UserId).Select(x => new { x.UserId, x.FullName, x.Email, x.PhoneNumber, x.PasswordHash, x.AccountStatus, x.PasswordSetupTokenHash, x.PasswordSetupExpiresAt }).ToListAsync()));
        Assert.Equal(rolesBefore, await db.ApplicationUserRoles.OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.RoleId }).ToListAsync());
        Assert.Equal(1, await db.TrainerProfiles.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync());
    }

}
