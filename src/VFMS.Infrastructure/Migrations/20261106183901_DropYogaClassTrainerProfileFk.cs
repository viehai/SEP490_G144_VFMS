using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropYogaClassTrainerProfileFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YogaClasses_TrainerProfiles_InstructorUserId",
                table: "YogaClasses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TrainerProfiles_PTUserId",
                table: "TrainerProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrainerProfiles_PTUserId",
                table: "TrainerProfiles",
                column: "PTUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_YogaClasses_TrainerProfiles_InstructorUserId",
                table: "YogaClasses",
                column: "InstructorUserId",
                principalTable: "TrainerProfiles",
                principalColumn: "PTUserId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
