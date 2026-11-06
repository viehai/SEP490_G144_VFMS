using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Application.Services;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class SqlTrainerFactAttribute : FactAttribute
{
    public SqlTrainerFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VFMS_TEST_SQL"))) Skip = "Set VFMS_TEST_SQL for isolated SQL Server integration tests."; }
}
public class TrainerWorkflowTests
{
    [SqlTrainerFact]
    public async Task Migration_and_recruitment_workflow_preserve_privacy_and_idempotency()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VFMS_TEST_SQL"))
        {
            InitialCatalog = "VFMS_TrainerTest_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 10
        };
        var options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlServer(builder.ConnectionString).Options;
        var protection = new EphemeralDataProtectionProvider();
        await using var db = new VfmsDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261104021354_FixPolicyAndBannedWordFK");
            // Old data must receive distinct tracking codes before the unique index is introduced.
            await db.Database.ExecuteSqlRawAsync("INSERT INTO TrainerApplications (FullName,Email,PhoneNumber,CitizenId,ApplicationFileUrl,ApplicationStatus,SubmittedAt,SubmissionAttempt,CreatedAt,UpdatedAt,IsDeleted) VALUES ('Legacy A','a@example.test','0900000001','000000000001','','Rejected',GETUTCDATE(),1,GETUTCDATE(),GETUTCDATE(),0), ('Legacy B','b@example.test','0900000002','000000000002','','Rejected',GETUTCDATE(),1,GETUTCDATE(),GETUTCDATE(),0)");
            await db.Database.MigrateAsync();
            var codes = await db.TrainerApplications.Select(x => x.TrackingCode).ToListAsync(); Assert.Equal(2, codes.Distinct().Count()); Assert.All(codes, x => Assert.StartsWith("TR-", x));
            var gym = new Gym { GymName = "Synthetic test gym" }; db.Add(gym); await db.SaveChangesAsync();
            var manager = new ApplicationUser { UserId = "manager", GymId = gym.Id, FullName = "Test Manager", Email = "manager@example.test", PhoneNumber = "0900000099", AccountStatus = AccountStatus.Active };
            var managerRole = new ApplicationRole { RoleName = "Manager" }; var ptRole = new ApplicationRole { RoleName = "PT" }; db.AddRange(manager, managerRole, ptRole); await db.SaveChangesAsync();
            db.Add(new ApplicationUserRole { UserId = manager.UserId, RoleId = managerRole.Id }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var service = new TrainerService(new TrainerStore(db, protection), new PasswordHasher()); var ct = CancellationToken.None;
            var request = new SubmitTrainerApplicationRequest("Test Trainer", "trainer@example.test", "0900000010", "000000000010", new(2000, 1, 1), "Other", "Strength", 3, "Strength coach", true, [new("proof.pdf", "%PDF-1.7 synthetic"u8.ToArray())]);
            var submitted = await service.Submit(request, null, ct); Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
            Assert.False((await service.Submit(request, null, ct)).IsSuccess);
            Assert.Equal(1, await db.ApplicationUsers.CountAsync());
            var app = await db.TrainerApplications.SingleAsync(x => x.Email == request.Email); var id = app.Id;
            var file = await db.Set<TrainerDocument>().SingleAsync(x => x.TrainerApplicationId == id);
            Assert.False((await service.Document(file.Id, null, ct)).IsSuccess);
            Assert.True((await service.Document(file.Id, "manager", ct)).IsSuccess);
            Assert.False((await service.Review(id, new("Hire", "Verified", "Junior", "Tier1", null, true), "manager", ct)).IsSuccess);
            Assert.True((await service.Review(id, new("Interview", "Interview completed"), "manager", ct)).IsSuccess);
            var hired = await service.Review(id, new("Hire", "Identity checked", "Junior", "Tier1", null, true), "manager", ct); Assert.True(hired.IsSuccess, hired.ErrorMessage);
            Assert.True((await service.Review(id, new("Hire", "Retry", "Junior", "Tier1", null, true), "manager", ct)).IsSuccess);
            Assert.Equal(1, await db.TrainerProfiles.CountAsync()); Assert.Equal(2, await db.ApplicationUsers.CountAsync());
            var search = await service.Search(new(), ct); Assert.True(search.IsSuccess); Assert.Empty(search.Data!.Items);
            var mail = await db.Set<TrainerEmail>().SingleAsync(x => x.Subject == "Set up your trainer account");
            var raw = protection.CreateProtector("VFMS.TrainerEmail.v1").Unprotect(mail.ProtectedBody)[6..];
            Assert.True((await service.Setup(new(raw, "TestPassword1!"), ct)).IsSuccess);
            Assert.False((await service.Setup(new(raw, "OtherPassword1!"), ct)).IsSuccess);
            search = await service.Search(new(Specialty: "Strength", MinimumRating: 0), ct); Assert.Single(search.Data!.Items);
            var profile = search.Data.Items.Single(); var publicDetail = await service.Public(profile.Id, ct); Assert.True(publicDetail.IsSuccess, publicDetail.ErrorMessage);
            Assert.Single(publicDetail.Data!.Certificates); Assert.True((await service.Document(publicDetail.Data.Certificates.Single().Id, null, ct)).IsSuccess);
            var change = await service.RequestCredentials(new("Yoga", [new("yoga.pdf", "%PDF-1.7 yoga"u8.ToArray())]), profile.UserId, ct); Assert.True(change.IsSuccess, change.ErrorMessage);
            Assert.Equal("Strength", (await service.Public(profile.Id, ct)).Data!.Profile.Specialty);
            Assert.False((await service.ReviewCredentials(change.Data, new(true, "Approve"), profile.UserId, ct)).IsSuccess);
            var managerChanges = await service.Changes("manager", 1, 20, ct); Assert.Single(managerChanges.Data!.Items);
            Assert.True((await service.ReviewCredentials(change.Data, new(true, "Verified"), "manager", ct)).IsSuccess);
            Assert.Equal("Yoga", (await service.Public(profile.Id, ct)).Data!.Profile.Specialty);
            Assert.False((await service.Document(publicDetail.Data.Certificates.Single().Id, null, ct)).IsSuccess);
            Assert.True((await service.Restrict(new("blocked@example.test", "000000000011", true, "Verified restriction"), "manager", ct)).IsSuccess);
            var blocked = await service.Submit(request with { Email = "blocked@example.test", CitizenId = "000000000011" }, null, ct); Assert.Equal("RECRUITMENT_RESTRICTED", blocked.ErrorCode);
            Assert.True(await db.AuditLogs.AnyAsync());
            // Hiring an existing customer preserves their account and role; it cannot silently merge identities.
            var customerRole = new ApplicationRole { RoleName = "Customer" }; db.Add(customerRole);
            var customer = new ApplicationUser { UserId = "customer", GymId = gym.Id, FullName = "Existing Customer", Email = "customer@example.test", PhoneNumber = "0900000050", AccountStatus = AccountStatus.Active };
            db.Add(customer); await db.SaveChangesAsync(); db.Add(new ApplicationUserRole { UserId = customer.UserId, RoleId = customerRole.Id }); await db.SaveChangesAsync();
            var existingRequest = request with { Email = customer.Email, PhoneNumber = customer.PhoneNumber, CitizenId = "000000000050" };
            Assert.True((await service.Submit(existingRequest, customer.UserId, ct)).IsSuccess);
            var existingId = await db.TrainerApplications.Where(x => x.Email == customer.Email).Select(x => x.Id).SingleAsync();
            Assert.True((await service.Review(existingId, new("Interview", "Verified interview"), "manager", ct)).IsSuccess);
            Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(existingId, new("Hire", "Verified", "Senior", "Tier2", null, true), "manager", ct)).ErrorCode);
            Assert.True((await service.Review(existingId, new("Hire", "Verified", "Senior", "Tier2", customer.UserId, true), "manager", ct)).IsSuccess);
            Assert.Equal(3, await db.ApplicationUsers.CountAsync());
            Assert.Equal(2, await db.ApplicationUserRoles.CountAsync(x => x.UserId == customer.UserId));
            Assert.True((await service.Mine(customer.UserId, ct)).IsSuccess);
            Assert.True((await service.Changes(customer.UserId, 1, 20, ct)).IsSuccess);
            var currentManager = await db.ApplicationUsers.SingleAsync(x => x.UserId == "manager"); currentManager.IsBanned = true; await db.SaveChangesAsync();
            Assert.Equal("TRAINER_FORBIDDEN", (await service.Applications(new(), "manager", ct)).ErrorCode);
            // New applicants after cooldown can reuse an account without a unique ApplicantUserId constraint.
            Assert.False(db.Model.FindEntityType(typeof(TrainerApplication))!.GetForeignKeys().Single(x => x.Properties.Any(p => p.Name == "ApplicantUserId")).IsUnique);
        }
        finally
        {
            // The only deletion target is the uniquely named disposable database created by this test.
            Assert.StartsWith("VFMS_TrainerTest_", builder.InitialCatalog); await db.Database.EnsureDeletedAsync();
        }
    }
}
