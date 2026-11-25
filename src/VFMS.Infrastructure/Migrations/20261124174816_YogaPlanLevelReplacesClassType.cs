using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YogaPlanLevelReplacesClassType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageComponents_YogaClassTypes_YogaClassTypeId",
                table: "PackageComponents");

            migrationBuilder.DropForeignKey(
                name: "FK_YogaWaitlists_YogaClassTypes_YogaClassTypeId",
                table: "YogaWaitlists");

            migrationBuilder.DropIndex(
                name: "IX_YogaWaitlists_YogaClassTypeId",
                table: "YogaWaitlists");

            migrationBuilder.DropIndex(
                name: "IX_PackageComponents_YogaClassTypeId",
                table: "PackageComponents");

            migrationBuilder.DropColumn(
                name: "YogaClassTypeId",
                table: "YogaWaitlists");

            migrationBuilder.DropColumn(
                name: "YogaClassTypeId",
                table: "PackageComponents");

            migrationBuilder.AddColumn<string>(
                name: "YogaDifficultyLevel",
                table: "PackageComponents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YogaDifficultyLevel",
                table: "PackageComponents");

            migrationBuilder.AddColumn<int>(
                name: "YogaClassTypeId",
                table: "YogaWaitlists",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YogaClassTypeId",
                table: "PackageComponents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaWaitlists_YogaClassTypeId",
                table: "YogaWaitlists",
                column: "YogaClassTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageComponents_YogaClassTypeId",
                table: "PackageComponents",
                column: "YogaClassTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PackageComponents_YogaClassTypes_YogaClassTypeId",
                table: "PackageComponents",
                column: "YogaClassTypeId",
                principalTable: "YogaClassTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_YogaWaitlists_YogaClassTypes_YogaClassTypeId",
                table: "YogaWaitlists",
                column: "YogaClassTypeId",
                principalTable: "YogaClassTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
