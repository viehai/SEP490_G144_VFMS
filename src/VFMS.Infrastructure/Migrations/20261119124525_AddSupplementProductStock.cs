using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplementProductStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "SupplementProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplementProducts_StockQuantity",
                table: "SupplementProducts",
                sql: "[StockQuantity] IS NULL OR [StockQuantity] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplementProducts_StockQuantity",
                table: "SupplementProducts");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "SupplementProducts");
        }
    }
}
