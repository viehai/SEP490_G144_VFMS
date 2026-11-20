using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplementProductBrandSizeFlavour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "SupplementProducts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Flavour",
                table: "SupplementProducts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Size",
                table: "SupplementProducts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplementProducts_Brand",
                table: "SupplementProducts",
                column: "Brand");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplementProducts_Brand",
                table: "SupplementProducts");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "SupplementProducts");

            migrationBuilder.DropColumn(
                name: "Flavour",
                table: "SupplementProducts");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "SupplementProducts");
        }
    }
}
