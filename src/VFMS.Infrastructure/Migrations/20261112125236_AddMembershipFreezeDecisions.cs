using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipFreezeDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppliedExpiryExtensionDays",
                table: "MembershipFreezes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyUnfreezeReviewNote",
                table: "MembershipFreezes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EarlyUnfreezeReviewedAt",
                table: "MembershipFreezes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes",
                column: "EarlyUnfreezeReviewedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipFreezes_ApplicationUsers_EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes",
                column: "EarlyUnfreezeReviewedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipFreezes_ApplicationUsers_EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "AppliedExpiryExtensionDays",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "EarlyUnfreezeReviewNote",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "EarlyUnfreezeReviewedAt",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "EarlyUnfreezeReviewedByUserId",
                table: "MembershipFreezes");
        }
    }
}
