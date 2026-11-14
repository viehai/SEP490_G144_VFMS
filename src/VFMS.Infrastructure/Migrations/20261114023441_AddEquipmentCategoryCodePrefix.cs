using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCategoryCodePrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodePrefix",
                table: "CatalogCategories",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_CodePrefix",
                table: "CatalogCategories",
                column: "CodePrefix",
                unique: true,
                filter: "[CodePrefix] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogCategories_CodePrefix",
                table: "CatalogCategories");

            migrationBuilder.DropColumn(
                name: "CodePrefix",
                table: "CatalogCategories");
        }
    }
}
