using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkFeedbackToPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackageId",
                table: "TrainerReviews",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_PackageId_MemberUserId",
                table: "TrainerReviews",
                columns: new[] { "PackageId", "MemberUserId" },
                unique: true,
                filter: "[PackageId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerReviews_Packages_PackageId",
                table: "TrainerReviews",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainerReviews_Packages_PackageId",
                table: "TrainerReviews");

            migrationBuilder.DropIndex(
                name: "IX_TrainerReviews_PackageId_MemberUserId",
                table: "TrainerReviews");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "TrainerReviews");
        }
    }
}
