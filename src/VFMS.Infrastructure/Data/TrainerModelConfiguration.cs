using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;

namespace VFMS.Infrastructure.Data;

internal static class TrainerModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<TrainerApplication>(e =>
        {
            e.Property(x => x.TrackingCode).HasMaxLength(40).IsRequired(); e.HasIndex(x => x.TrackingCode).IsUnique();
            e.Property(x => x.Gender).HasMaxLength(20); e.Property(x => x.Specialty).HasMaxLength(200); e.Property(x => x.Bio).HasMaxLength(4000);
            e.Property(x => x.ReviewNote).HasMaxLength(2000);
            e.HasIndex(x => new { x.Email, x.SubmittedAt }); e.HasIndex(x => new { x.CitizenId, x.SubmittedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.HiredUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrainerProfile>(e => { e.Property(x => x.Specialty).HasMaxLength(200); e.Property(x => x.Gender).HasMaxLength(20); e.Property(x => x.Bio).HasMaxLength(4000); e.Property(x => x.CommissionTier).HasMaxLength(100); });
        b.Entity<TrainerDocument>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.ContentType).HasMaxLength(100); e.Property(x => x.Content).HasColumnType("varbinary(max)");
            e.HasOne<TrainerApplication>().WithMany().HasForeignKey(x => x.TrainerApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainerCredentialChange>().WithMany().HasForeignKey(x => x.TrainerCredentialChangeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrainerCredentialChange>(e =>
        {
            e.Property(x => x.Specialty).HasMaxLength(200); e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.ReviewNote).HasMaxLength(2000);
            e.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TrainerProfileId).IsUnique().HasFilter("[Status] = 'Pending' AND [IsDeleted] = 0");
        });
        b.Entity<TrainerRecruitmentRestriction>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.CitizenId).HasMaxLength(12); e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasIndex(x => x.Email).IsUnique(); e.HasIndex(x => x.CitizenId).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrainerSetupToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrainerEmail>(e => { e.Property(x => x.Recipient).HasMaxLength(256); e.Property(x => x.Subject).HasMaxLength(200); e.HasIndex(x => new { x.SentAt, x.NextAttemptAt }); });
    }
}
