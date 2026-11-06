using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YogaSchedulingAndWorkoutTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstructorUserId",
                table: "YogaClassSchedules",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DifficultyLevel",
                table: "YogaClasses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "YogaClasses",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationDeadline",
                table: "YogaClasses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "SessionsPerWeek",
                table: "YogaClasses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "YogaClasses",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "MatNumber",
                table: "YogaBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "PlanItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "PlanItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassSchedules_InstructorUserId",
                table: "YogaClassSchedules",
                column: "InstructorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_YogaClassScheduleId_MatNumber",
                table: "YogaBookings",
                columns: new[] { "YogaClassScheduleId", "MatNumber" },
                unique: true,
                filter: "[MatNumber] IS NOT NULL AND [BookingStatus] = 'Confirmed'");

            migrationBuilder.AddForeignKey(
                name: "FK_YogaClassSchedules_ApplicationUsers_InstructorUserId",
                table: "YogaClassSchedules",
                column: "InstructorUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YogaClassSchedules_ApplicationUsers_InstructorUserId",
                table: "YogaClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_YogaClassSchedules_InstructorUserId",
                table: "YogaClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_YogaBookings_YogaClassScheduleId_MatNumber",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "InstructorUserId",
                table: "YogaClassSchedules");

            migrationBuilder.DropColumn(
                name: "DifficultyLevel",
                table: "YogaClasses");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "YogaClasses");

            migrationBuilder.DropColumn(
                name: "RegistrationDeadline",
                table: "YogaClasses");

            migrationBuilder.DropColumn(
                name: "SessionsPerWeek",
                table: "YogaClasses");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "YogaClasses");

            migrationBuilder.DropColumn(
                name: "MatNumber",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "PlanItems");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "PlanItems");
        }
    }
}
