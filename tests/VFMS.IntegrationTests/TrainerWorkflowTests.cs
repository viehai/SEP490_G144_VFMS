using System.Text.Json;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

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
            ["JwtSettings:Issuer"] = "vfms-tests",
            ["JwtSettings:Audience"] = "vfms-tests"
        }).Build();
        var mapping = new TypeAdapterConfig();
        mapping.Scan(typeof(TrainerMapping).Assembly);
        Service = new(new TrainerStore(Db, Protection, Configuration), new PasswordHasher(), new Mapper(mapping));
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
    public static SubmitTrainerApplicationRequest Request => new("Test Trainer", "trainer@example.test", "0900000010", "000000000010", new(2000, 1, 1), "Other", "PT", 3, "Strength coach", true, [new("proof.pdf", "%PDF-1.7 synthetic"u8.ToArray())]);
    public async Task<TrainerApplication> Hire()
    {
        Success(await Service.Submit(Request, null, default));
        var app = await Db.TrainerApplications.SingleAsync();
        Success(await Service.Review(app.Id, new("Interview", "Interview completed"), "manager", default));
        Success(await Service.Review(app.Id, new("Hire", "Verified", "Junior", null, true), "manager", default));
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
    public async Task TrainerSetupEmailUsesSharedEmailServiceAndFrontendUrl()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var application = await fixture.Hire();
        var token = await fixture.SetupToken(application.Id);

        fixture.Configuration["PasswordReset:ResetUrl"] = "https://gym.example.test/auth/reset-password";
        var email = new CapturedTrainerEmail();
        var services = new ServiceCollection();
        services.AddDbContext<VfmsDbContext>(options => options.UseSqlServer(fixture.Db.Database.GetConnectionString()));
        services.AddSingleton<IDataProtectionProvider>(fixture.Protection);
        services.AddSingleton<IEmailService>(email);
        await using var provider = services.BuildServiceProvider();
        using var worker = new TrainerEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Configuration, NullLogger<TrainerEmailWorker>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(default);
        try
        {
            var message = await email.Delivery.Task.WaitAsync(timeout.Token);
            Assert.Equal(TrainerTestDatabase.Request.Email, message.Recipient);
            Assert.Equal("Set up your trainer account", message.Subject);
            Assert.Contains($"https://gym.example.test/auth/trainer-setup?token={Uri.EscapeDataString(token)}", message.Body);

            while (true)
            {
                var json = await fixture.Db.TrainerApplications.Where(x => x.Id == application.Id)
                    .Select(x => x.EmailQueueJson).SingleAsync(timeout.Token);
                var setup = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(json)!.Single(x => x.Subject == message.Subject);
                if (setup.SentAt is not null)
                {
                    Assert.Empty(setup.ProtectedBody);
                    break;
                }
                await Task.Delay(50, timeout.Token);
            }
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    private sealed class CapturedTrainerEmail : IEmailService
    {
        public bool IsConfigured => true;
        public TaskCompletionSource<(string Recipient, string Subject, string Body)> Delivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default)
        {
            Delivery.TrySetResult((recipient, subject, textBody));
            return Task.CompletedTask;
        }
    }

    [SqlTrainerFact]
    public async Task Verified_existing_account_can_update_with_current_email()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service; var request = TrainerTestDatabase.Request;
        var customerRole = new ApplicationRole { RoleName = "Customer" };
        var customer = new ApplicationUser
        {
            UserId = "existing-customer",
            GymId = await db.Gyms.Select(x => x.Id).SingleAsync(),
            FullName = "Existing Customer",
            Email = "current@example.test",
            PhoneNumber = request.PhoneNumber,
            PasswordHash = new PasswordHasher().Hash("OriginalPassword1!"),
            AccountStatus = AccountStatus.Active
        };
        db.AddRange(customerRole, customer); await db.SaveChangesAsync();
        db.Add(new ApplicationUserRole { UserId = customer.UserId, RoleId = customerRole.Id }); await db.SaveChangesAsync();
        var passwordBefore = customer.PasswordHash;
        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        var original = await db.TrainerApplications.SingleAsync();
        TrainerTestDatabase.Success(await service.Review(original.Id, new("Interview", "Identity checked"), "manager", default));
        var detail = TrainerTestDatabase.Success(await service.Application(original.Id, "manager", default));
        Assert.Equal(new TrainerAccountMatch("Matched", customer.UserId, customer.FullName), detail.AccountMatch);
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(original.Id, new("Hire", "Stale match", "Senior", null, true), "manager", default)).ErrorCode);
        TrainerTestDatabase.Success(await service.Review(original.Id,
            new("Hire", "Verified matching phone", "Senior", detail.AccountMatch!.UserId, true), "manager", default));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
        // UC 3.2 step 7: the linked account keeps only the PT role.
        Assert.Equal(["PT"], await db.ApplicationUserRoles.Where(x => x.UserId == customer.UserId)
            .Select(x => x.Role.RoleName).ToListAsync());
        var update = request with { ApplicationType = "ProfileUpdate", Email = customer.Email, Specialty = "Yoga" };
        TrainerTestDatabase.Success(await service.Submit(update, customer.UserId, default));
        var firstUpdate = await db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate");
        TrainerTestDatabase.Success(await service.Review(firstUpdate.Id, new("Reject", "Resubmit certificate"), "manager", default));
        Assert.Equal("TRAINER_PROFILE_UPDATE_COOLDOWN", (await service.Submit(update, customer.UserId, default)).ErrorCode);
        firstUpdate = await db.TrainerApplications.SingleAsync(x => x.Id == firstUpdate.Id);
        firstUpdate.SubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();
        var retried = TrainerTestDatabase.Success(await service.Submit(update, customer.UserId, default));
        Assert.Equal(2, retried.SubmissionAttempt);
        var unchangedAccount = await db.ApplicationUsers.SingleAsync(x => x.UserId == customer.UserId);
        Assert.Equal(passwordBefore, unchangedAccount.PasswordHash); Assert.Equal(customer.Email, unchangedAccount.Email);
        Assert.Null(unchangedAccount.PasswordSetupTokenHash); Assert.Equal(AccountStatus.Active, unchangedAccount.AccountStatus);
        Assert.Equal(1, await db.ApplicationUserRoles.CountAsync(x => x.UserId == customer.UserId));
    }

    [SqlTrainerFact]
    public async Task PT_not_accepting_for_more_than_60_days_loses_only_the_PT_role()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var hired = await fixture.Hire();
        TrainerTestDatabase.Success(await fixture.Service.Setup(
            new(await fixture.SetupToken(hired.Id), "TestPassword1!"), default));
        var db = fixture.Db;
        var profile = await db.TrainerProfiles.SingleAsync();
        profile.NotAcceptingSince = new DateOnly(2026, 1, 1);
        profile.AcceptingNewMembers = false;
        await db.SaveChangesAsync();
        var workerService = new PersonalTrainingService(new PersonalTrainingStore(db), TimeProvider.System, null!, null!, null!);
        await workerService.ExpireInactiveTrainers(new DateOnly(2026, 3, 2), default);
        Assert.True((await db.TrainerProfiles.AsNoTracking().SingleAsync()).IsActive);
        await workerService.ExpireInactiveTrainers(new DateOnly(2026, 3, 3), default);
        Assert.False((await db.TrainerProfiles.SingleAsync()).IsActive);
        Assert.DoesNotContain(await db.ApplicationUserRoles.Where(x => x.UserId == profile.PTUserId)
            .Select(x => x.Role.RoleName).ToListAsync(), role => role == "PT");
        Assert.Equal(AccountStatus.Active, (await db.ApplicationUsers.SingleAsync(x => x.UserId == profile.PTUserId)).AccountStatus);

        // The removed trainer can apply again and be rehired: the old profile is reactivated with the PT role.
        foreach (var previous in await db.TrainerApplications.ToListAsync())
            previous.SubmittedAt = DateTime.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();
        TrainerTestDatabase.Success(await fixture.Service.Submit(TrainerTestDatabase.Request, null, default));
        var again = await db.TrainerApplications.OrderByDescending(x => x.Id).FirstAsync();
        TrainerTestDatabase.Success(await fixture.Service.Review(again.Id, new("Interview", "Returning trainer"), "manager", default));
        var match = TrainerTestDatabase.Success(await fixture.Service.Application(again.Id, "manager", default)).AccountMatch;
        Assert.Equal(("Matched", profile.PTUserId), (match!.Status, match.UserId));
        TrainerTestDatabase.Success(await fixture.Service.Review(again.Id,
            new("Hire", "Rehired", "Master", profile.PTUserId, true), "manager", default));
        var rehired = await db.TrainerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal((profile.Id, true, true, TrainerLevel.Master), (rehired.Id, rehired.IsActive, rehired.AcceptingNewMembers, rehired.Level));
        Assert.Null(rehired.NotAcceptingSince);
        Assert.Equal(["PT"], await db.ApplicationUserRoles.Where(x => x.UserId == profile.PTUserId)
            .Select(x => x.Role.RoleName).ToListAsync());
    }

    [SqlTrainerFact]
    public async Task PT_accepting_status_changes_immediately_and_tracks_the_not_accepting_start()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var hired = await fixture.Hire();
        var service = fixture.Service;
        TrainerTestDatabase.Success(await service.Setup(new(await fixture.SetupToken(hired.Id), "TestPassword1!"), default));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        // New trainers start 'Accepting New Members', so the GBR-98 60-day count does not run.
        var stored = await fixture.Db.TrainerProfiles.AsNoTracking().SingleAsync();
        Assert.True(stored.AcceptingNewMembers);
        Assert.Null(stored.NotAcceptingSince);

        var stopped = TrainerTestDatabase.Success(await service.SetReadiness(false, stored.PTUserId, default));
        Assert.False(stopped.IsAcceptingNewMembers);
        Assert.Equal(0, stopped.ContractsNotified);
        Assert.Equal(today, (await fixture.Db.TrainerProfiles.AsNoTracking().SingleAsync()).NotAcceptingSince);

        var accepting = TrainerTestDatabase.Success(await service.SetReadiness(true, stored.PTUserId, default));
        Assert.True(accepting.IsAcceptingNewMembers);
        var profile = TrainerTestDatabase.Success(await service.Mine(stored.PTUserId, default)).Profile;
        Assert.True(profile.IsAcceptingNewMembers);
        Assert.Null(profile.NotAcceptingSince);
    }

    [SqlTrainerFact]
    public async Task Profile_update_can_replace_and_add_certificate_urls_after_manager_approval()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var service = fixture.Service;
        var hired = await fixture.Hire();
        TrainerTestDatabase.Success(await service.Setup(new(await fixture.SetupToken(hired.Id), "TestPassword1!"), default));
        var userId = (await fixture.Db.TrainerProfiles.SingleAsync()).PTUserId;
        var before = TrainerTestDatabase.Success(await service.Mine(userId, default));
        var oldId = Assert.Single(before.Certificates).Id;
        var request = TrainerTestDatabase.Request with
        {
            ApplicationType = "ProfileUpdate", Certificates = [], CertificateUrls = null,
            CertificateLinks = [new("https://certificates.example.test/revised", oldId),
                new("https://certificates.example.test/additional")]
        };
        TrainerTestDatabase.Success(await service.Submit(request, userId, default));
        var application = await fixture.Db.TrainerApplications.SingleAsync(x => x.ApplicationType == "ProfileUpdate");
        Assert.DoesNotContain(TrainerTestDatabase.Success(await service.Public(before.Profile.Id, default)).Certificates,
            certificate => certificate.IsExternalLink);
        // The Manager sees which published certificate each link replaces.
        var pending = TrainerTestDatabase.Success(await service.Application(application.Id, "manager", default)).Certificates;
        Assert.Equal(before.Certificates[0].FileName,
            pending.Single(c => c.DownloadUrl.EndsWith("/revised")).Replaces);
        Assert.Null(pending.Single(c => c.DownloadUrl.EndsWith("/additional")).Replaces);
        TrainerTestDatabase.Success(await service.Review(application.Id, new("Approve", "Verified links"), "manager", default));
        var certificates = TrainerTestDatabase.Success(await service.Public(before.Profile.Id, default)).Certificates;
        Assert.Equal(2, certificates.Count);
        Assert.DoesNotContain(certificates, certificate => certificate.Id == oldId);
        Assert.Contains(certificates, certificate => certificate.DownloadUrl == "https://certificates.example.test/revised");
        Assert.Contains(certificates, certificate => certificate.DownloadUrl == "https://certificates.example.test/additional");
    }

    [SqlTrainerFact]
    public async Task Signed_in_member_without_customer_role_can_apply()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var db = fixture.Db;
        var memberRole = await db.ApplicationRoles.SingleOrDefaultAsync(r => r.RoleName == "Member") ?? new ApplicationRole { RoleName = "Member" };
        var member = new ApplicationUser
        {
            UserId = "member-only", GymId = await db.Gyms.Select(x => x.Id).SingleAsync(), FullName = "Member Only",
            Email = "member.only@example.test", PhoneNumber = "0900000099",
            PasswordHash = new PasswordHasher().Hash("OriginalPassword1!"), AccountStatus = AccountStatus.Active
        };
        db.AddRange(memberRole, member); await db.SaveChangesAsync();
        db.Add(new ApplicationUserRole { UserId = member.UserId, RoleId = memberRole.Id }); await db.SaveChangesAsync();

        var receipt = TrainerTestDatabase.Success(await fixture.Service.Submit(TrainerTestDatabase.Request with
        {
            Email = member.Email, PhoneNumber = member.PhoneNumber, CitizenId = "000000000099"
        }, member.UserId, default));
        Assert.Equal("Pending", receipt.Status);
    }

    [SqlTrainerFact]
    public async Task Manager_can_pause_and_resume_new_recruitment_submissions()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var service = fixture.Service;
        Assert.True(TrainerTestDatabase.Success(await service.RecruitmentStatus(default)).IsAcceptingApplications);
        TrainerTestDatabase.Success(await service.SetRecruitmentStatus(false, "manager", default));
        Assert.False(TrainerTestDatabase.Success(await service.RecruitmentStatus(default)).IsAcceptingApplications);
        Assert.Equal("TRAINER_RECRUITMENT_CLOSED",
            (await service.Submit(TrainerTestDatabase.Request, null, default)).ErrorCode);
        TrainerTestDatabase.Success(await service.SetRecruitmentStatus(true, "manager", default));
        TrainerTestDatabase.Success(await service.Submit(TrainerTestDatabase.Request, null, default));
    }

    [SqlTrainerFact]
    public async Task RecruitmentCertificateLinkIsPrivateUntilHireAndAccountConflictsBlockHire()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var db = fixture.Db;
        var service = fixture.Service;
        const string url = "https://certificates.example.test/verify/123";
        var request = TrainerTestDatabase.Request with { Certificates = [], CertificateUrls = [url] };
        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        var application = await db.TrainerApplications.SingleAsync();
        var review = TrainerTestDatabase.Success(await service.Application(application.Id, "manager", default));
        Assert.Equal("None", review.AccountMatch!.Status);
        var evidence = Assert.Single(review.Certificates);
        Assert.True(evidence.IsExternalLink);
        Assert.Equal(url, evidence.DownloadUrl);
        Assert.Equal(404, (await service.ApplicationDocument(application.Id, evidence.Id, null, default)).StatusCode);
        Assert.Empty(TrainerTestDatabase.Success(await service.Search(new(), default)).Items);
        TrainerTestDatabase.Success(await service.Review(application.Id, new("Interview", "Verified"), "manager", default));

        var gymId = await db.Gyms.Select(g => g.Id).SingleAsync();
        var emailMatch = new ApplicationUser
        {
            UserId = "email-match", GymId = gymId, Email = request.Email, PhoneNumber = "0900000020",
            FullName = "Email Match", AccountStatus = AccountStatus.Active
        };
        var phoneMatch = new ApplicationUser
        {
            UserId = "phone-match", GymId = gymId, Email = "phone@example.test", PhoneNumber = request.PhoneNumber,
            FullName = "Phone Match", AccountStatus = AccountStatus.Active
        };
        db.AddRange(emailMatch, phoneMatch);
        await db.SaveChangesAsync();
        var ambiguous = TrainerTestDatabase.Success(await service.Application(application.Id, "manager", default)).AccountMatch!;
        Assert.Equal("Ambiguous", ambiguous.Status);
        Assert.NotNull(ambiguous.Matches);
        Assert.Collection(ambiguous.Matches!.OrderBy(match => match.UserId),
            match =>
            {
                Assert.Equal("email-match", match.UserId);
                Assert.Equal("Email Match", match.FullName);
                Assert.Equal(request.Email, match.Email);
                Assert.Equal("0900000020", match.PhoneNumber);
                Assert.True(match.EmailMatches);
                Assert.False(match.PhoneMatches);
            },
            match =>
            {
                Assert.Equal("phone-match", match.UserId);
                Assert.Equal("Phone Match", match.FullName);
                Assert.Equal("phone@example.test", match.Email);
                Assert.Equal(request.PhoneNumber, match.PhoneNumber);
                Assert.False(match.EmailMatches);
                Assert.True(match.PhoneMatches);
            });
        var blocked = await service.Review(application.Id, new("Hire", "Verified", "Senior", IdentityVerified: true), "manager", default);
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", blocked.ErrorCode);

        db.Remove(await db.ApplicationUsers.SingleAsync(u => u.UserId == "phone-match"));
        await db.SaveChangesAsync();
        var match = TrainerTestDatabase.Success(await service.Application(application.Id, "manager", default)).AccountMatch!;
        TrainerTestDatabase.Success(await service.Review(application.Id,
            new("Hire", "Verified", "Senior", match.UserId, true), "manager", default));
        var profile = await db.TrainerProfiles.SingleAsync();
        Assert.Null(profile.CommissionTier);
        var published = TrainerTestDatabase.Success(await service.Public(profile.Id, default));
        Assert.False(published.IsOwnProfile);
        Assert.True(TrainerTestDatabase.Success(await service.Public(profile.Id, default, match.UserId)).IsOwnProfile);
        Assert.Equal(evidence, Assert.Single(published.Certificates));
    }

    [SqlTrainerFact]
    public async Task RecruitmentCooldownMatchesEitherIdentityAndAllowsPendingResubmissionAfterTenMinutes()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var request = TrainerTestDatabase.Request;
        var service = fixture.Service;
        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(request with { CitizenId = "000000000011" }, null, default)).ErrorCode);
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(request with { Email = "second@example.test" }, null, default)).ErrorCode);

        var original = await fixture.Db.TrainerApplications.SingleAsync();
        original.SubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        await fixture.Db.SaveChangesAsync();
        var resubmission = TrainerTestDatabase.Success(await service.Submit(request with { Email = "second@example.test" }, null, default));
        Assert.Equal(2, resubmission.SubmissionAttempt);
        Assert.Equal(2, await fixture.Db.TrainerApplications.CountAsync());
    }

    [SqlTrainerFact]
    public async Task RecruitmentUsesTheSameGmailIdentityForCooldownHistoryAndHiring()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var db = fixture.Db;
        var service = fixture.Service;
        var request = TrainerTestDatabase.Request with { Email = "trainer.test@gmail.com" };
        var alias = request with { Email = "trainertest@gmail.com", CitizenId = "000000000011", PhoneNumber = "0900000011" };
        var gymId = await db.Gyms.Select(gym => gym.Id).SingleAsync();
        db.Add(new ApplicationUser
        {
            UserId = "gmail-existing", GymId = gymId, Email = request.Email, PhoneNumber = "0900000020",
            FullName = "Existing applicant", AccountStatus = AccountStatus.Active
        });
        await db.SaveChangesAsync();

        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(alias, null, default)).ErrorCode);
        var original = await db.TrainerApplications.SingleAsync();
        original.SubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var receipt = TrainerTestDatabase.Success(await service.Submit(alias, null, default));
        Assert.Equal(2, receipt.SubmissionAttempt);
        var latest = await db.TrainerApplications.SingleAsync(item => item.TrackingCode == receipt.TrackingCode);
        var detail = TrainerTestDatabase.Success(await service.Application(latest.Id, "manager", default));
        Assert.Equal(new[] { latest.Id, original.Id }, detail.SubmissionHistory!.Select(item => item.Id));
        Assert.Equal("gmail-existing", detail.AccountMatch!.UserId);

        TrainerTestDatabase.Success(await service.Review(latest.Id, new("Interview", "Verified"), "manager", default));
        TrainerTestDatabase.Success(await service.Review(latest.Id, new("Hire", "Verified", "Junior", detail.AccountMatch.UserId, true), "manager", default));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
        Assert.Equal("gmail-existing", (await db.TrainerProfiles.SingleAsync()).PTUserId);
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
        Assert.Equal("TRAINER_INTERVIEW_REQUIRED", (await service.Review(app.Id, new("Hire", "Verified", "Junior", null, true), "manager", default)).ErrorCode);
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Interview", "Interview completed"), "manager", default));
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Hire", "Verified", "Junior", null, true), "manager", default));
        TrainerTestDatabase.Success(await service.Review(app.Id, new("Hire", "Retry", "Junior", null, true), "manager", default));
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
        var managerPasswordBefore = await db.ApplicationUsers.Where(x => x.UserId == "manager").Select(x => x.PasswordHash).SingleAsync();
        TrainerTestDatabase.Success(await service.Setup(new(replacementToken, "TestPassword1!"), default));
        Assert.Equal(managerPasswordBefore,
            await db.ApplicationUsers.Where(x => x.UserId == "manager").Select(x => x.PasswordHash).SingleAsync());
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
        Assert.Equal("PT", stillPublic.Profile.Specialty); Assert.Equal(oldFile.Id, stillPublic.Certificates.Single().Id);
        Assert.Single(TrainerTestDatabase.Success(await service.Search(new(Specialty: "PT"), default)).Items);
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
        pending = await db.TrainerApplications.SingleAsync(x => x.Id == pending.Id);
        pending.SubmittedAt = DateTime.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();
        TrainerTestDatabase.Success(await service.Submit(update with { Specialty = "PT" }, userId, default));
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
        rejected = await db.TrainerApplications.SingleAsync(x => x.Id == rejected.Id);
        rejected.SubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();
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

    private static async Task<ApplicationUser> AddUser(VfmsDbContext db, string id, string email, string phone, string? role = null)
    {
        var user = new ApplicationUser
        {
            UserId = id, GymId = await db.Gyms.Select(g => g.Id).SingleAsync(), FullName = id,
            Email = email, PhoneNumber = phone, AccountStatus = AccountStatus.Active
        };
        db.Add(user);
        if (role != null)
        {
            var roleEntity = await db.ApplicationRoles.SingleOrDefaultAsync(r => r.RoleName == role) ?? new ApplicationRole { RoleName = role };
            db.Add(new ApplicationUserRole { UserId = id, Role = roleEntity });
        }
        await db.SaveChangesAsync();
        return user;
    }

    [SqlTrainerFact]
    public async Task Employees_cannot_apply_and_hiring_stores_citizen_id_and_closes_other_open_applications()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service; var request = TrainerTestDatabase.Request;
        await AddUser(db, "staff", "staff@example.test", "0900000050", "Staff");
        // GBR-101: Staff email (any Gmail dot variant included) cannot apply.
        Assert.Equal("TRAINER_NOT_ELIGIBLE", (await service.Submit(request with { Email = "staff@example.test" }, null, default)).ErrorCode);

        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        var first = await db.TrainerApplications.SingleAsync();
        first.SubmittedAt = DateTime.UtcNow.AddMinutes(-10); await db.SaveChangesAsync();
        TrainerTestDatabase.Success(await service.Submit(request with { Email = "second@example.test" }, null, default));
        var second = await db.TrainerApplications.SingleAsync(x => x.Id != first.Id);
        TrainerTestDatabase.Success(await service.Review(second.Id, new("Interview", "Interview completed"), "manager", default));
        TrainerTestDatabase.Success(await service.Review(second.Id, new("Hire", "Verified", "Junior", null, true), "manager", default));

        db.ChangeTracker.Clear();
        var closed = await db.TrainerApplications.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(TrainerApplicationStatus.Rejected, closed.ApplicationStatus);
        Assert.Contains("Closed automatically", closed.ReviewNote);
        Assert.Equal(request.CitizenId, (await db.TrainerProfiles.SingleAsync()).CitizenId);

        // An active trainer's Citizen ID blocks a new application under another email.
        TrainerTestDatabase.Success(await service.Setup(new(await fixture.SetupToken(second.Id), "TestPassword1!"), default));
        await db.TrainerApplications.ForEachAsync(x => x.SubmittedAt = DateTime.UtcNow.AddMinutes(-20));
        await db.SaveChangesAsync();
        Assert.Equal("TRAINER_NOT_ELIGIBLE", (await service.Submit(request with { Email = "third@example.test" }, null, default)).ErrorCode);
    }

    [SqlTrainerFact]
    public async Task Manager_picks_one_eligible_account_when_application_matches_several()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service; var request = TrainerTestDatabase.Request;
        TrainerTestDatabase.Success(await service.Submit(request, null, default));
        var application = await db.TrainerApplications.SingleAsync();
        TrainerTestDatabase.Success(await service.Review(application.Id, new("Interview", "Interview completed"), "manager", default));
        await AddUser(db, "email-match", request.Email, "0900000020", "Customer");
        await AddUser(db, "phone-staff", "phone@example.test", request.PhoneNumber, "Staff");

        var match = TrainerTestDatabase.Success(await service.Application(application.Id, "manager", default)).AccountMatch!;
        Assert.Equal("Ambiguous", match.Status);
        Assert.True(match.Matches!.Single(m => m.UserId == "email-match").Eligible);
        Assert.False(match.Matches!.Single(m => m.UserId == "phone-staff").Eligible);

        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(application.Id,
            new("Hire", "Verified", "Senior", "phone-staff", true), "manager", default)).ErrorCode);
        TrainerTestDatabase.Success(await service.Review(application.Id,
            new("Hire", "Verified in person", "Senior", "email-match", true), "manager", default));
        Assert.Equal("email-match", (await db.TrainerProfiles.SingleAsync()).PTUserId);
        Assert.Equal(3, await db.ApplicationUsers.CountAsync());
    }

    [SqlTrainerFact]
    public async Task Expired_setup_link_notifies_managers_once_per_link()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service;
        var hired = await fixture.Hire();
        Assert.Equal(0, TrainerTestDatabase.Success(await service.NotifyExpiredSetupLinks(default)));

        var user = await db.ApplicationUsers.SingleAsync(x => x.UserId == hired.HiredUserId);
        user.PasswordSetupExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        Assert.Equal(1, TrainerTestDatabase.Success(await service.NotifyExpiredSetupLinks(default)));
        Assert.Equal(0, TrainerTestDatabase.Success(await service.NotifyExpiredSetupLinks(default)));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RecipientUserId == "manager" && n.NotificationType == "TrainerSetupLinkExpired"));
    }

    [SqlTrainerFact]
    public async Task Banned_bio_lists_offending_words_and_reviews_show_member_name()
    {
        await using var fixture = new TrainerTestDatabase(); await fixture.Initialize();
        var db = fixture.Db; var service = fixture.Service;
        var hired = await fixture.Hire();
        TrainerTestDatabase.Success(await service.Setup(new(await fixture.SetupToken(hired.Id), "TestPassword1!"), default));
        var profile = await db.TrainerProfiles.SingleAsync();
        db.Add(new BannedWordList { Word = "scam", CreatedByUserId = "manager" });
        var member = await AddUser(db, "member", "member@example.test", "0900000060", "Member");
        db.Add(new TrainerReview { TrainerUserId = profile.PTUserId, MemberUserId = member.UserId, Rating = 5, Comment = "Great coach",
            ReviewStatus = TrainerReviewStatus.Approved, IsVisible = true });
        await db.SaveChangesAsync();

        var rejected = await service.Update(new("Not a SCAM coach"), profile.PTUserId, default);
        Assert.Equal(TrainerContentRules.RejectedCode, rejected.ErrorCode);
        Assert.Equal(["scam"], TrainerContentRules.ProhibitedWords(rejected.ErrorCode, rejected.ErrorMessage));

        var review = Assert.Single(TrainerTestDatabase.Success(await service.Public(profile.Id, default)).Reviews);
        Assert.Equal("member", review.MemberName);
    }
}
