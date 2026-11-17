using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPTTrainerAbsenceReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SessionStatus",
                table: "PTSessions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "StatusBeforeTrainerAbsenceReport",
                table: "PTSessions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrainerAbsenceApproved",
                table: "PTSessions",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainerAbsenceReason",
                table: "PTSessions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrainerAbsenceReportedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainerAbsenceReviewNote",
                table: "PTSessions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrainerAbsenceReviewedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusBeforeTrainerAbsenceReport",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceApproved",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceReason",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceReportedAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceReviewNote",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceReviewedAt",
                table: "PTSessions");

            migrationBuilder.AlterColumn<string>(
                name: "SessionStatus",
                table: "PTSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);
        }
    }
}
