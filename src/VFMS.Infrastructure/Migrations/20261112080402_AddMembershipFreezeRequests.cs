using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipFreezeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliedPolicySnapshot",
                table: "MembershipFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyUnfreezeRequestStatus",
                table: "MembershipFreezes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EarlyUnfreezeRequestedAt",
                table: "MembershipFreezes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreezePolicyId",
                table: "MembershipFreezes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "MembershipFreezes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_FreezePolicyId",
                table: "MembershipFreezes",
                column: "FreezePolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipFreezes_FreezePolicies_FreezePolicyId",
                table: "MembershipFreezes",
                column: "FreezePolicyId",
                principalTable: "FreezePolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipFreezes_FreezePolicies_FreezePolicyId",
                table: "MembershipFreezes");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_FreezePolicyId",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "AppliedPolicySnapshot",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "EarlyUnfreezeRequestStatus",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "EarlyUnfreezeRequestedAt",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "FreezePolicyId",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "MembershipFreezes");
        }
    }
}
