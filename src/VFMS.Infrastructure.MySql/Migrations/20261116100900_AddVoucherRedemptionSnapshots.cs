using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VFMS.Infrastructure.MySql.Migrations;

[DbContext(typeof(VfmsMySqlDbContext))]
[Migration("20261116100900_AddVoucherRedemptionSnapshots")]
public sealed class AddVoucherRedemptionSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
                name: "VoucherCodeSnapshot",
                table: "VoucherRedemptions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
                name: "VoucherNameSnapshot",
                table: "VoucherRedemptions",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "VoucherCodeSnapshot",
            table: "VoucherRedemptions");

        migrationBuilder.DropColumn(
            name: "VoucherNameSnapshot",
            table: "VoucherRedemptions");
    }
}
