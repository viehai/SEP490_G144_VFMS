using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignPersonalTrainingWithSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Readiness changes now apply immediately (GBR-104): settle scheduled changes that are already due.
            migrationBuilder.Sql("""
                UPDATE TrainerProfiles SET AcceptingNewMembers = PendingAcceptingNewMembers
                WHERE PendingAcceptingNewMembers IS NOT NULL
                  AND AcceptingNewMembersEffectiveDate <= CAST(DATEADD(HOUR, 7, SYSUTCDATETIME()) AS date);
                """);

            migrationBuilder.RenameColumn(
                name: "FirstReadyDate",
                table: "TrainerProfiles",
                newName: "NotAcceptingSince");

            // The 60-day 'Not Accepting' period starts today for trainers who are not accepting.
            migrationBuilder.Sql("""
                UPDATE TrainerProfiles SET NotAcceptingSince = CASE WHEN AcceptingNewMembers = 1 THEN NULL
                    ELSE CAST(DATEADD(HOUR, 7, SYSUTCDATETIME()) AS date) END;
                """);

            migrationBuilder.DropColumn(
                name: "AcceptingNewMembersEffectiveDate",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "PendingAcceptingNewMembers",
                table: "TrainerProfiles");

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceReminderSentAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrainerChangeDeadline",
                table: "PTPurchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerRemindedAt",
                table: "PTFreezes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementTrainerId",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceReminderSentAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerChangeDeadline",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ManagerRemindedAt",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ReplacementTrainerId",
                table: "PTFreezes");

            migrationBuilder.RenameColumn(
                name: "NotAcceptingSince",
                table: "TrainerProfiles",
                newName: "FirstReadyDate");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptingNewMembersEffectiveDate",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingAcceptingNewMembers",
                table: "TrainerProfiles",
                type: "bit",
                nullable: true);
        }
    }
}
