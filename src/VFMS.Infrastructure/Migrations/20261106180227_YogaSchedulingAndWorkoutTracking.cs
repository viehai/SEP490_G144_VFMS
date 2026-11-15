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
            migrationBuilder.DropForeignKey(
                name: "FK_YogaBookings_YogaClassSchedules_YogaClassScheduleId",
                table: "YogaBookings");

            migrationBuilder.DropIndex(
                name: "IX_YogaBookings_YogaClassScheduleId_MemberUserId",
                table: "YogaBookings");

            migrationBuilder.DropIndex(
                name: "IX_YogaAttendances_YogaBookingId",
                table: "YogaAttendances");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlans_MemberUserId",
                table: "WorkoutPlans");

            migrationBuilder.RenameColumn(
                name: "YogaClassScheduleId",
                table: "YogaBookings",
                newName: "YogaClassId");

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

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "YogaBookings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatNumber",
                table: "YogaBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderItemId",
                table: "YogaBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "YogaBookings",
                type: "date",
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
                name: "IX_YogaBookings_OrderItemId",
                table: "YogaBookings",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_YogaClassId_MatNumber",
                table: "YogaBookings",
                columns: new[] { "YogaClassId", "MatNumber" },
                unique: true,
                filter: "[MatNumber] IS NOT NULL AND [BookingStatus] = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_YogaClassId_MemberUserId",
                table: "YogaBookings",
                columns: new[] { "YogaClassId", "MemberUserId" },
                unique: true,
                filter: "[BookingStatus] = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_YogaBookingId",
                table: "YogaAttendances",
                column: "YogaBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlans_MemberUserId_ActivePlan",
                table: "WorkoutPlans",
                column: "MemberUserId",
                unique: true,
                filter: "[PlanStatus] = 'Active' AND [IsTemplate] = 0 AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_YogaBookings_OrderItems_OrderItemId",
                table: "YogaBookings",
                column: "OrderItemId",
                principalTable: "OrderItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_YogaBookings_YogaClasses_YogaClassId",
                table: "YogaBookings",
                column: "YogaClassId",
                principalTable: "YogaClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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
                name: "FK_YogaBookings_OrderItems_OrderItemId",
                table: "YogaBookings");

            migrationBuilder.DropForeignKey(
                name: "FK_YogaBookings_YogaClasses_YogaClassId",
                table: "YogaBookings");

            migrationBuilder.DropForeignKey(
                name: "FK_YogaClassSchedules_ApplicationUsers_InstructorUserId",
                table: "YogaClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_YogaClassSchedules_InstructorUserId",
                table: "YogaClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_YogaBookings_OrderItemId",
                table: "YogaBookings");

            migrationBuilder.DropIndex(
                name: "IX_YogaBookings_YogaClassId_MatNumber",
                table: "YogaBookings");

            migrationBuilder.DropIndex(
                name: "IX_YogaBookings_YogaClassId_MemberUserId",
                table: "YogaBookings");

            migrationBuilder.DropIndex(
                name: "IX_YogaAttendances_YogaBookingId",
                table: "YogaAttendances");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlans_MemberUserId_ActivePlan",
                table: "WorkoutPlans");

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
                name: "EndDate",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "MatNumber",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "OrderItemId",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "YogaBookings");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "PlanItems");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "PlanItems");

            migrationBuilder.RenameColumn(
                name: "YogaClassId",
                table: "YogaBookings",
                newName: "YogaClassScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_YogaClassScheduleId_MemberUserId",
                table: "YogaBookings",
                columns: new[] { "YogaClassScheduleId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_YogaBookingId",
                table: "YogaAttendances",
                column: "YogaBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlans_MemberUserId",
                table: "WorkoutPlans",
                column: "MemberUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_YogaBookings_YogaClassSchedules_YogaClassScheduleId",
                table: "YogaBookings",
                column: "YogaClassScheduleId",
                principalTable: "YogaClassSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
