using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerRecruitmentAndReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptingNewMembers",
                table: "TrainerProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptingNewMembersEffectiveDate",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FirstReadyDate",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingAcceptingNewMembers",
                table: "TrainerProfiles",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAcceptingTrainerApplications",
                table: "Gyms",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE TrainerProfiles SET AcceptingNewMembers = 1, FirstReadyDate = CONVERT(date, DATEADD(hour, 7, SYSUTCDATETIME())) WHERE IsActive = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptingNewMembers",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "AcceptingNewMembersEffectiveDate",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "FirstReadyDate",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "PendingAcceptingNewMembers",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "IsAcceptingTrainerApplications",
                table: "Gyms");
        }
    }
}
