using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerInterviewNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InterviewNote",
                table: "TrainerApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [TrainerApplications]
                    SET [InterviewNote] = [ReviewNote]
                    WHERE [ApplicationType] = N''Recruitment'' AND [ApplicationStatus] = N''Interviewing''
                        AND [ReviewNote] IS NOT NULL;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterviewNote",
                table: "TrainerApplications");
        }
    }
}
