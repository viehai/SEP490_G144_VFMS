using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerProfileCitizenId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CitizenId",
                table: "TrainerProfiles",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            // Backfill from each trainer's hired recruitment application; one profile per Citizen ID keeps the unique index valid.
            migrationBuilder.Sql("""
                WITH hired AS (
                    SELECT p.Id AS ProfileId, a.CitizenId,
                           ROW_NUMBER() OVER (PARTITION BY p.Id ORDER BY a.SubmittedAt, a.Id) AS PerProfile
                    FROM TrainerProfiles p
                    JOIN TrainerApplications a ON a.HiredUserId = p.PTUserId
                    WHERE a.ApplicationType = 'Recruitment' AND a.ApplicationStatus = 'Hired' AND a.IsDeleted = 0 AND a.CitizenId <> ''
                ), ranked AS (
                    SELECT ProfileId, CitizenId,
                           ROW_NUMBER() OVER (PARTITION BY CitizenId ORDER BY ProfileId) AS PerCitizen
                    FROM hired WHERE PerProfile = 1
                )
                UPDATE p SET CitizenId = r.CitizenId
                FROM TrainerProfiles p JOIN ranked r ON r.ProfileId = p.Id
                WHERE r.PerCitizen = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerProfiles_CitizenId",
                table: "TrainerProfiles",
                column: "CitizenId",
                unique: true,
                filter: "[CitizenId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainerProfiles_CitizenId",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "CitizenId",
                table: "TrainerProfiles");
        }
    }
}
