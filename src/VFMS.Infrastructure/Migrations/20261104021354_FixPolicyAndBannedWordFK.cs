using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPolicyAndBannedWordFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                table: "BannedWordLists");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "FreezePolicies",
                type: "nvarchar(128)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "BannedWordLists",
                type: "nvarchar(128)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_FreezePolicies_CreatedByUserId",
                table: "FreezePolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedWordLists_CreatedByUserId",
                table: "BannedWordLists",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BannedWordLists_ApplicationUsers_CreatedByUserId",
                table: "BannedWordLists",
                column: "CreatedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FreezePolicies_ApplicationUsers_CreatedByUserId",
                table: "FreezePolicies",
                column: "CreatedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BannedWordLists_ApplicationUsers_CreatedByUserId",
                table: "BannedWordLists");

            migrationBuilder.DropForeignKey(
                name: "FK_FreezePolicies_ApplicationUsers_CreatedByUserId",
                table: "FreezePolicies");

            migrationBuilder.DropIndex(
                name: "IX_FreezePolicies_CreatedByUserId",
                table: "FreezePolicies");

            migrationBuilder.DropIndex(
                name: "IX_BannedWordLists_CreatedByUserId",
                table: "BannedWordLists");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "FreezePolicies");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "BannedWordLists");

            migrationBuilder.AddColumn<string>(
                name: "AddedByUserId",
                table: "BannedWordLists",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
