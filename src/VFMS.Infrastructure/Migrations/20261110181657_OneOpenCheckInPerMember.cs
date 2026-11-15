using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OneOpenCheckInPerMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckIns_MemberUserId",
                table: "CheckIns");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_MemberUserId",
                table: "CheckIns",
                column: "MemberUserId",
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckIns_MemberUserId",
                table: "CheckIns");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_MemberUserId",
                table: "CheckIns",
                column: "MemberUserId");
        }
    }
}
