using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherTransactionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoucherRedemptions_VoucherId",
                table: "VoucherRedemptions");

            migrationBuilder.AddColumn<string>(
                name: "VoucherCodeSnapshot",
                table: "VoucherRedemptions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoucherNameSnapshot",
                table: "VoucherRedemptions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_VoucherId_RedemptionStatus",
                table: "VoucherRedemptions",
                columns: new[] { "VoucherId", "RedemptionStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoucherRedemptions_VoucherId_RedemptionStatus",
                table: "VoucherRedemptions");

            migrationBuilder.DropColumn(
                name: "VoucherCodeSnapshot",
                table: "VoucherRedemptions");

            migrationBuilder.DropColumn(
                name: "VoucherNameSnapshot",
                table: "VoucherRedemptions");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_VoucherId",
                table: "VoucherRedemptions",
                column: "VoucherId");
        }
    }
}
