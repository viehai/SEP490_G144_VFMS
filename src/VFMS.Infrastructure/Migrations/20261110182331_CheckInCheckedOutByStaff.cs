using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckInCheckedOutByStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckedOutByUserId",
                table: "CheckIns",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_CheckedOutByUserId",
                table: "CheckIns",
                column: "CheckedOutByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CheckIns_ApplicationUsers_CheckedOutByUserId",
                table: "CheckIns",
                column: "CheckedOutByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckIns_ApplicationUsers_CheckedOutByUserId",
                table: "CheckIns");

            migrationBuilder.DropIndex(
                name: "IX_CheckIns_CheckedOutByUserId",
                table: "CheckIns");

            migrationBuilder.DropColumn(
                name: "CheckedOutByUserId",
                table: "CheckIns");
        }
    }
}
