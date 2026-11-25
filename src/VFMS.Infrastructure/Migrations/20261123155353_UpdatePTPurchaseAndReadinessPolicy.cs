using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePTPurchaseAndReadinessPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptingSince",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotAcceptingAccumulatedDays",
                table: "TrainerProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Earlier accepting periods were not recorded; begin their ten-day window at the policy change.
            migrationBuilder.Sql("UPDATE TrainerProfiles SET AcceptingSince = CONVERT(date, DATEADD(hour, 7, SYSUTCDATETIME())) WHERE AcceptingNewMembers = 1 AND IsActive = 1");

            migrationBuilder.AlterColumn<int>(
                name: "MemberMembershipId",
                table: "PTPurchases",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PTPurchases WHERE MemberMembershipId IS NULL) THROW 51000, 'Cannot restore the Gym membership requirement while PT purchases without a Gym membership exist.', 1;");

            migrationBuilder.DropColumn(
                name: "AcceptingSince",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "NotAcceptingAccumulatedDays",
                table: "TrainerProfiles");

            migrationBuilder.AlterColumn<int>(
                name: "MemberMembershipId",
                table: "PTPurchases",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
