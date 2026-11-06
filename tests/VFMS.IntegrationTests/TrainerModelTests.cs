using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public class TrainerModelTests
{
    [Fact]
    public void Applicant_can_submit_multiple_applications_and_tracking_code_is_unique()
    {
        using var db = new VfmsDbContext(new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Integrated Security=true").Options);
        var model = db.Model.FindEntityType(typeof(TrainerApplication))!;
        var fk = model.GetForeignKeys().Single(x => x.Properties.Any(p => p.Name == "ApplicantUserId"));
        Assert.False(fk.IsUnique);
        Assert.Contains(model.GetIndexes(), i => !i.IsUnique && i.GetDatabaseName() == "IX_TrainerApplications_ApplicantUserId");
        Assert.Contains(model.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == "TrackingCode"));
        Assert.Contains(model.GetIndexes(), i => i.IsUnique && i.GetDatabaseName()!.Contains("PendingProfileUpdate") && i.GetFilter() != null);
    }

    [Fact]
    public void Trainer_records_are_json_values_and_removed_tables_are_not_mapped()
    {
        using var db = new VfmsDbContext(new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Integrated Security=true").Options);
        string[] removed = ["TrainerDocument", "TrainerCredentialChange", "TrainerEmail", "TrainerSetupToken", "TrainerRecruitmentRestriction"];
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => removed.Contains(e.GetTableName()));
        Assert.Null(db.Model.FindEntityType(typeof(TrainerStoredFile)));
        Assert.Null(db.Model.FindEntityType(typeof(TrainerQueuedEmail)));
        var application = db.Model.FindEntityType(typeof(TrainerApplication))!;
        Assert.NotNull(application.FindProperty("CertificatesJson"));
        Assert.NotNull(application.FindProperty("EmailQueueJson"));
        Assert.NotNull(application.FindProperty("IsRecruitmentRestricted"));
        var profile = db.Model.FindEntityType(typeof(TrainerProfile))!;
        Assert.NotNull(profile.FindProperty("CertificatesJson"));
        Assert.NotNull(profile.FindProperty("AvatarJson"));
        Assert.Null(profile.FindProperty("AvatarFileId"));
        var user = db.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.NotNull(user.FindProperty("PasswordSetupTokenHash"));
        Assert.NotNull(user.FindProperty("PasswordSetupExpiresAt"));
    }
}
