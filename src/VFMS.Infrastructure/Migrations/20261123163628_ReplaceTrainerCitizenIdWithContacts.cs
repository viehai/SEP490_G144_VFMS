using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceTrainerCitizenIdWithContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainerProfiles_CitizenId",
                table: "TrainerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_CitizenId_SubmittedAt",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "CitizenId",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "CitizenId",
                table: "TrainerApplications");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_PhoneNumber_SubmittedAt",
                table: "TrainerApplications",
                columns: new[] { "PhoneNumber", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_PhoneNumber_SubmittedAt",
                table: "TrainerApplications");

            migrationBuilder.AddColumn<string>(
                name: "CitizenId",
                table: "TrainerProfiles",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CitizenId",
                table: "TrainerApplications",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerProfiles_CitizenId",
                table: "TrainerProfiles",
                column: "CitizenId",
                unique: true,
                filter: "[CitizenId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_CitizenId_SubmittedAt",
                table: "TrainerApplications",
                columns: new[] { "CitizenId", "SubmittedAt" });
        }
    }
}
