using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTrainerApplicationRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainerApplications_ApplicationUsers_RestrictionChangedByUserId",
                table: "TrainerApplications");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_RestrictionChangedByUserId",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "IsRecruitmentRestricted",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "RestrictionChangedAt",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "RestrictionChangedByUserId",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "RestrictionReason",
                table: "TrainerApplications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRecruitmentRestricted",
                table: "TrainerApplications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestrictionChangedAt",
                table: "TrainerApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionChangedByUserId",
                table: "TrainerApplications",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionReason",
                table: "TrainerApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_RestrictionChangedByUserId",
                table: "TrainerApplications",
                column: "RestrictionChangedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerApplications_ApplicationUsers_RestrictionChangedByUserId",
                table: "TrainerApplications",
                column: "RestrictionChangedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
