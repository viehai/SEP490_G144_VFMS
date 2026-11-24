using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherAudienceAndCompetitionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "Vouchers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AllCustomers");

            migrationBuilder.AddColumn<int>(
                name: "NewCustomerDuration",
                table: "Vouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewCustomerUnit",
                table: "Vouchers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoucherGrantId",
                table: "VoucherRedemptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VoucherGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionPrizeId = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherGrants_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherGrants_CompetitionPrizes_CompetitionPrizeId",
                        column: x => x.CompetitionPrizeId,
                        principalTable: "CompetitionPrizes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherGrants_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_VoucherGrantId",
                table: "VoucherRedemptions",
                column: "VoucherGrantId",
                unique: true,
                filter: "[VoucherGrantId] IS NOT NULL AND [RedemptionStatus] <> 'Released'");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherGrants_CompetitionPrizeId_UserId",
                table: "VoucherGrants",
                columns: new[] { "CompetitionPrizeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherGrants_UserId_VoucherId",
                table: "VoucherGrants",
                columns: new[] { "UserId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherGrants_VoucherId",
                table: "VoucherGrants",
                column: "VoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherRedemptions_VoucherGrants_VoucherGrantId",
                table: "VoucherRedemptions",
                column: "VoucherGrantId",
                principalTable: "VoucherGrants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VoucherRedemptions_VoucherGrants_VoucherGrantId",
                table: "VoucherRedemptions");

            migrationBuilder.DropTable(
                name: "VoucherGrants");

            migrationBuilder.DropIndex(
                name: "IX_VoucherRedemptions_VoucherGrantId",
                table: "VoucherRedemptions");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "NewCustomerDuration",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "NewCustomerUnit",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "VoucherGrantId",
                table: "VoucherRedemptions");
        }
    }
}
