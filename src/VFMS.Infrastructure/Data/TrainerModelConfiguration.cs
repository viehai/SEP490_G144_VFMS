using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;

namespace VFMS.Infrastructure.Data;

internal static class TrainerModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.PasswordSetupTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.PasswordSetupTokenHash).IsUnique().HasFilter("[PasswordSetupTokenHash] IS NOT NULL");
        });
        b.Entity<TrainerApplication>(e =>
        {
            e.Property(x => x.TrackingCode).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.TrackingCode).IsUnique();
            e.Property(x => x.Gender).HasMaxLength(20);
            e.Property(x => x.Specialty).HasMaxLength(200);
            e.Property(x => x.Bio).HasMaxLength(4000);
            e.Property(x => x.ReviewNote).HasMaxLength(2000);
            e.Property(x => x.InterviewNote).HasMaxLength(2000);
            e.Property(x => x.ApplicationType).HasMaxLength(20);
            e.HasIndex(x => new { x.Email, x.SubmittedAt }); e.HasIndex(x => new { x.CitizenId, x.SubmittedAt });
            e.HasIndex(x => x.ApplicantUserId).IsUnique(false);
            e.HasIndex(x => x.ApplicantUserId, "PendingProfileUpdate").IsUnique().HasDatabaseName("IX_TrainerApplications_PendingProfileUpdate")
                .HasFilter("[ApplicationType] = 'ProfileUpdate' AND [ApplicationStatus] = 'Pending' AND [IsDeleted] = 0");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.HiredUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrainerProfile>(e =>
        {
            e.Property(x => x.Specialty).HasMaxLength(200);
            e.Property(x => x.Gender).HasMaxLength(20);
            e.Property(x => x.Bio).HasMaxLength(4000);
            e.Property(x => x.CommissionTier).HasMaxLength(100);
        });
    }
}
