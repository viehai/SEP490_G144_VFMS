using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public class TrainerMigrationTests
{
    [SqlTrainerFact]
    public async Task Nonempty_legacy_tables_preserve_documents_reviews_restrictions_tokens_and_mail()
    {
        await using var fixture = new TrainerTestDatabase(); var db = fixture.Db;
        await db.GetService<IMigrator>().MigrateAsync("20261106152017_TrainerRecruitmentAndProfiles");
        // Seed the actual old schema: the current EF model cannot write the removed entities.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            INSERT INTO Gyms (GymName, IsAcceptingMembers, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('Legacy gym', 1, @now, @now, 0);
            DECLARE @gym int = SCOPE_IDENTITY();
            INSERT INTO ApplicationUsers (UserId, GymId, FullName, Email, PhoneNumber, PasswordHash, AccountStatus, IsBanned, Id, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy-pt', @gym, 'Legacy PT', 'legacy@example.test', '0900000001', 'preserve-password', 'PendingVerification', 0, 1, @now, @now, 0),
                       ('legacy-manager', @gym, 'Legacy Manager', 'manager@example.test', '0900000002', 'manager-password', 'Active', 0, 2, @now, @now, 0);
            INSERT INTO TrainerApplications (ApplicantUserId, HiredUserId, FullName, Email, PhoneNumber, CitizenId, ApplicationFileUrl, ApplicationStatus, SubmittedAt, SubmissionAttempt, TrackingCode, Specialty, Gender, ExperienceYears, Bio, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy-pt', 'legacy-pt', 'Legacy PT', 'legacy@example.test', '0900000001', '000000000001', '', 'Hired', DATEADD(day,-60,@now), 1, 'TR-LEGACY-HIRED', 'Strength', 'Other', 5, 'Legacy biography', @now, @now, 0);
            DECLARE @app int = SCOPE_IDENTITY();
            INSERT INTO TrainerProfiles (PTUserId, Bio, Level, CommissionTier, IsActive, Specialty, Gender, ExperienceYears, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy-pt', 'Legacy biography', 'Senior', 'Tier2', 1, 'Strength', 'Other', 5, @now, @now, 0);
            DECLARE @profile int = SCOPE_IDENTITY();
            INSERT INTO TrainerCredentialChange (TrainerProfileId, Specialty, Status, ReviewedByUserId, ReviewedAt, ReviewNote, CreatedAt, UpdatedAt, IsDeleted)
                VALUES (@profile, 'Yoga', 'Pending', NULL, NULL, NULL, DATEADD(day,-1,@now), @now, 0);
            DECLARE @pending int = SCOPE_IDENTITY();
            INSERT INTO TrainerCredentialChange (TrainerProfileId, Specialty, Status, ReviewedByUserId, ReviewedAt, ReviewNote, CreatedAt, UpdatedAt, IsDeleted)
                VALUES (@profile, 'Strength', 'Approved', 'legacy-manager', DATEADD(day,-2,@now), 'Original approval', DATEADD(day,-3,@now), @now, 0),
                       (@profile, 'Pilates', 'Rejected', 'legacy-manager', DATEADD(day,-4,@now), 'Original rejection', DATEADD(day,-5,@now), @now, 0);
            INSERT INTO TrainerDocument (TrainerApplicationId, TrainerProfileId, TrainerCredentialChangeId, FileName, ContentType, Content, IsPublic, IsAvatar, CreatedAt, UpdatedAt, IsDeleted)
                VALUES (@app, @profile, NULL, 'legacy.pdf', 'application/pdf', 0x255044462D312E37206C6567616379, 1, 0, @now, @now, 0),
                       (NULL, @profile, @pending, 'pending.pdf', 'application/pdf', 0x255044462D312E372070656E64696E67, 0, 0, @now, @now, 0),
                       (NULL, @profile, NULL, 'avatar.png', 'image/png', 0x89504E470D0A1A0A, 1, 1, @now, @now, 0);
            DECLARE @avatar int = SCOPE_IDENTITY();
            UPDATE TrainerProfiles SET AvatarFileId=@avatar WHERE Id=@profile;
            INSERT INTO TrainerDocument (TrainerApplicationId, TrainerProfileId, FileName, ContentType, Content, IsPublic, IsAvatar, CreatedAt, UpdatedAt, IsDeleted)
                VALUES (@app, @profile, 'deleted.pdf', 'application/pdf', 0x255044462D312E372064656C65746564, 1, 0, @now, @now, 1);
            INSERT INTO TrainerRecruitmentRestriction (Email, CitizenId, IsActive, Reason, ChangedByUserId, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy@example.test', '000000000001', 1, 'Legacy restriction', 'legacy-manager', @now, @now, 0);
            INSERT INTO TrainerSetupToken (UserId, TokenHash, ExpiresAt, UsedAt, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy-pt', REPLICATE('A',64), DATEADD(hour,24,@now), NULL, DATEADD(hour,-2,@now), @now, 0),
                       ('legacy-pt', REPLICATE('B',64), DATEADD(hour,24,@now), NULL, DATEADD(hour,-1,@now), @now, 0),
                       ('legacy-pt', REPLICATE('C',64), DATEADD(hour,24,@now), @now, @now, @now, 0);
            INSERT INTO TrainerEmail (Recipient, Subject, ProtectedBody, Attempts, NextAttemptAt, SentAt, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy@example.test', 'Legacy queued message', 'opaque-protected-body', 2, DATEADD(minute,-5,@now), NULL, @now, @now, 0),
                       ('legacy@example.test', 'Legacy delivered message', 'sent-body', 1, @now, DATEADD(minute,-1,@now), @now, @now, 0);
            INSERT INTO TrainerEmail (Recipient, Subject, ProtectedBody, Attempts, NextAttemptAt, SentAt, CreatedAt, UpdatedAt, IsDeleted)
                VALUES ('legacy@example.test', 'Legacy deleted message', 'deleted-protected-body', 3, DATEADD(minute,-5,@now), NULL, @now, @now, 1);
            """);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var apps = await db.TrainerApplications.OrderBy(x => x.Id).ToListAsync();
        var original = Assert.Single(apps.Where(x => x.TrackingCode == "TR-LEGACY-HIRED"));
        Assert.Equal("Recruitment", original.ApplicationType);
        Assert.Equal(TrainerApplicationStatus.Hired, original.ApplicationStatus);
        Assert.True(original.IsRecruitmentRestricted);
        Assert.Equal("Legacy restriction", original.RestrictionReason);
        Assert.Equal("legacy-manager", original.RestrictionChangedByUserId);
        var changes = apps.Where(x => x.ApplicationType == "ProfileUpdate").ToList();
        Assert.Equal(3, changes.Count);
        Assert.All(changes, x => Assert.Equal("legacy-pt", x.ApplicantUserId));
        Assert.Equal(apps.Count, apps.Select(x => x.TrackingCode).Distinct().Count());
        var pending = Assert.Single(changes.Where(x => x.Specialty == "Yoga"));
        Assert.Equal(TrainerApplicationStatus.Pending, pending.ApplicationStatus);
        var approved = Assert.Single(changes.Where(x => x.Specialty == "Strength"));
        Assert.Equal(TrainerApplicationStatus.Approved, approved.ApplicationStatus);
        Assert.Equal("Original approval", approved.ReviewNote); Assert.Equal("legacy-manager", approved.ReviewedByUserId); Assert.NotNull(approved.ReviewedAt);
        var rejected = Assert.Single(changes.Where(x => x.Specialty == "Pilates"));
        Assert.Equal(TrainerApplicationStatus.Rejected, rejected.ApplicationStatus); Assert.Equal("Original rejection", rejected.ReviewNote);
        var profile = await db.TrainerProfiles.SingleAsync();
        Assert.Equal("Strength", profile.Specialty); Assert.Equal("Legacy biography", profile.Bio); Assert.Equal("Tier2", profile.CommissionTier);
        var store = new TrainerStore(db, fixture.Protection, fixture.Configuration);
        var preservedFiles = JsonSerializer.Deserialize<List<TrainerStoredFile>>(profile.CertificatesJson)!;
        Assert.Equal(4, preservedFiles.Count);
        var publicFile = Assert.Single(preservedFiles.Where(x => x.IsPublic && !x.IsAvatar));
        Assert.Equal("legacy.pdf", publicFile.FileName); Assert.True(publicFile.IsPublic);
        Assert.Equal("%PDF-1.7 legacy"u8.ToArray(), await store.ReadFile(publicFile, default));
        var originalFile = Assert.Single(JsonSerializer.Deserialize<List<TrainerStoredFile>>(original.CertificatesJson)!.Where(x => !x.IsDeleted));
        Assert.Equal(publicFile.Id, originalFile.Id);
        var pendingFile = Assert.Single(JsonSerializer.Deserialize<List<TrainerStoredFile>>(pending.CertificatesJson)!);
        Assert.False(pendingFile.IsPublic); Assert.Equal("%PDF-1.7 pending"u8.ToArray(), await store.ReadFile(pendingFile, default));
        Assert.False(Assert.Single(preservedFiles.Where(x => x.Id == pendingFile.Id)).IsPublic);
        var avatar = JsonSerializer.Deserialize<TrainerStoredFile>(profile.AvatarJson!)!;
        Assert.True(avatar.IsAvatar); Assert.Equal("avatar.png", avatar.FileName);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, await store.ReadFile(avatar, default));
        var user = await db.ApplicationUsers.SingleAsync(x => x.UserId == "legacy-pt");
        Assert.Equal("preserve-password", user.PasswordHash); Assert.Equal(new string('B', 64), user.PasswordSetupTokenHash); Assert.True(user.PasswordSetupExpiresAt > DateTime.UtcNow);
        var emails = apps.SelectMany(x => JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(x.EmailQueueJson)!).ToList();
        var queued = Assert.Single(emails.Where(x => x.Subject == "Legacy queued message"));
        Assert.Equal("opaque-protected-body", queued.ProtectedBody); Assert.Equal(2, queued.Attempts); Assert.Null(queued.SentAt);
        Assert.NotNull(Assert.Single(emails.Where(x => x.Subject == "Legacy delivered message")).SentAt);
        Assert.True(Assert.Single(emails.Where(x => x.Subject == "Legacy deleted message")).IsDeleted);
        var deletedFile = Assert.Single(preservedFiles.Where(x => x.FileName == "deleted.pdf"));
        Assert.True(deletedFile.IsDeleted);
        user.AccountStatus = AccountStatus.Active; await db.SaveChangesAsync();
        TrainerTestDatabase.Success(await fixture.Service.Document(profile.Id, publicFile.Id, null, default));
        Assert.Equal(404, (await fixture.Service.Document(profile.Id, deletedFile.Id, null, default)).StatusCode);
        var publicDetail = TrainerTestDatabase.Success(await fixture.Service.Public(profile.Id, default));
        Assert.Single(publicDetail.Certificates); Assert.DoesNotContain(publicDetail.Certificates, x => x.Id == deletedFile.Id);
        var auditPayloads = string.Join("\n", await db.AuditLogs.Select(x => (x.OldValue ?? "") + (x.NewValue ?? "")).ToListAsync());
        string[] sensitive = ["legacy@example.test", "000000000001", "0900000001", "preserve-password", "opaque-protected-body", "deleted-protected-body", "sent-body", new string('A', 64), new string('B', 64), new string('C', 64), Convert.ToBase64String("%PDF-1.7 legacy"u8.ToArray())];
        Assert.All(sensitive, value => Assert.DoesNotContain(value, auditPayloads));
        // Execute the worker's composable OPENJSON selection with a real queued row, without SMTP.
        const string workerQuery = "SELECT TOP (1) a.* FROM TrainerApplications a WHERE a.IsDeleted=0 AND EXISTS (SELECT 1 FROM OPENJSON(a.EmailQueueJson) WITH (SentAt datetime2 '$.SentAt', NextAttemptAt datetime2 '$.NextAttemptAt', IsDeleted bit '$.IsDeleted') e WHERE ISNULL(e.IsDeleted,0)=0 AND e.SentAt IS NULL AND e.NextAttemptAt <= SYSUTCDATETIME()) ORDER BY a.Id";
        var due = await db.TrainerApplications.FromSqlRaw(workerQuery).FirstOrDefaultAsync();
        Assert.NotNull(due);
        Assert.Contains(JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(due.EmailQueueJson)!, x => x.Id == queued.Id);
        foreach (var application in apps)
        {
            var queue = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(application.EmailQueueJson)!;
            foreach (var email in queue.Where(x => !x.IsDeleted && x.SentAt == null)) email.NextAttemptAt = DateTime.UtcNow.AddHours(1);
            application.EmailQueueJson = JsonSerializer.Serialize(queue);
        }
        await db.SaveChangesAsync();
        Assert.Null(await db.TrainerApplications.FromSqlRaw(workerQuery).FirstOrDefaultAsync());
        var removedCount = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('TrainerDocument','TrainerEmail','TrainerSetupToken','TrainerCredentialChange','TrainerRecruitmentRestriction')").SingleAsync();
        Assert.Equal(0, removedCount);
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
    }
}
