using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPTAttendanceRecordAndCheckInCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceRecordedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceRecordedPresent",
                table: "PTSessions",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInCompletedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceRecordedAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordedPresent",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "CheckInCompletedAt",
                table: "PTSessions");
        }
    }
}
