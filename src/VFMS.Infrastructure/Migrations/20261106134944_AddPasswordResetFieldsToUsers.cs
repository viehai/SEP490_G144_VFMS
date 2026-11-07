using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetFieldsToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResetTokenExpiresAt",
                table: "ApplicationUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetTokenHash",
                table: "ApplicationUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_ResetTokenHash",
                table: "ApplicationUsers",
                column: "ResetTokenHash",
                unique: true,
                filter: "[ResetTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_ResetTokenHash",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "ResetTokenExpiresAt",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "ResetTokenHash",
                table: "ApplicationUsers");
        }
    }
}
