using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignTrainerApplicationRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddTrainerRoles set every application to PT. Specialty used to hold the role ("PT"/"Yoga"), so an open
            // recruitment that applied for Yoga gets the Yoga Trainer Role and is hired as YogaInstructor (UC 3.1, 3.2).
            // Hired applications keep PT: those accounts already hold the PT login role.
            migrationBuilder.Sql("""
                UPDATE a SET TrainerRoleId = y.Id
                FROM TrainerApplications a
                CROSS JOIN (SELECT Id FROM TrainerRoles WHERE RoleName = 'Yoga') y
                WHERE a.ApplicationType = 'Recruitment' AND a.Specialty = 'Yoga'
                  AND a.ApplicationStatus IN ('Pending', 'UnderReview', 'Interviewing');

                UPDATE a SET TrainerRoleId = p.TrainerRoleId
                FROM TrainerApplications a
                JOIN TrainerProfiles p ON p.PTUserId = a.ApplicantUserId
                WHERE a.ApplicationType = 'ProfileUpdate' AND p.TrainerRoleId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE TrainerApplications SET TrainerRoleId = (SELECT Id FROM TrainerRoles WHERE RoleName = 'PT');
                """);
        }
    }
}
