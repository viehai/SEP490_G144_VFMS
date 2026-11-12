using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLockerRentalManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_CustomerUserId",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_LockerId",
                table: "LockerRentalContracts");

            migrationBuilder.AddColumn<DateTime>(
                name: "CollectBy",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalNoticeAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalNoticeEmailSentAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalNoticeRetryAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandedOverAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandedOverByUserId",
                table: "LockerRentalContracts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFeeSnapshot",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingAmount",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverdueDailyFeeSnapshot",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RentalTypeSnapshot",
                table: "LockerRentalContracts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettledByUserId",
                table: "LockerRentalContracts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFee",
                table: "LockerPricingPolicies",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverdueDailyFee",
                table: "LockerPricingPolicies",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LockerRentalContractId",
                table: "LockerIncidentLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "LockerIncidentLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByUserId",
                table: "LockerIncidentLogs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LockerRentalPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LockerRentalContractId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    SourceOrderId = table.Column<int>(type: "int", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoldExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RentalExpiryAtQuote = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalMonths = table.Column<int>(type: "int", nullable: true),
                    OverdueFees = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    KeyReplacementFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundReason = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RefundMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RefundReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerRentalPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerRentalPayments_LockerRentalContracts_LockerRentalContractId",
                        column: x => x.LockerRentalContractId,
                        principalTable: "LockerRentalContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerRentalPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerRentalPayments_Orders_SourceOrderId",
                        column: x => x.SourceOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_CustomerUserId_ContractStatus",
                table: "LockerRentalContracts",
                columns: new[] { "CustomerUserId", "ContractStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_LockerId_ContractStatus",
                table: "LockerRentalContracts",
                columns: new[] { "LockerId", "ContractStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerIncidentLogs_LockerRentalContractId",
                table: "LockerIncidentLogs",
                column: "LockerRentalContractId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalPayments_LockerRentalContractId_Purpose",
                table: "LockerRentalPayments",
                columns: new[] { "LockerRentalContractId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalPayments_OrderId",
                table: "LockerRentalPayments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalPayments_SourceOrderId",
                table: "LockerRentalPayments",
                column: "SourceOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_LockerIncidentLogs_LockerRentalContracts_LockerRentalContractId",
                table: "LockerIncidentLogs",
                column: "LockerRentalContractId",
                principalTable: "LockerRentalContracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LockerIncidentLogs_LockerRentalContracts_LockerRentalContractId",
                table: "LockerIncidentLogs");

            migrationBuilder.DropTable(
                name: "LockerRentalPayments");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_CustomerUserId_ContractStatus",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_LockerId_ContractStatus",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerIncidentLogs_LockerRentalContractId",
                table: "LockerIncidentLogs");

            migrationBuilder.DropColumn(
                name: "CollectBy",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "FinalNoticeAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "FinalNoticeEmailSentAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "FinalNoticeRetryAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "HandedOverAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "HandedOverByUserId",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFeeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "OutstandingAmount",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "OverdueDailyFeeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "RentalTypeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "SettledByUserId",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFee",
                table: "LockerPricingPolicies");

            migrationBuilder.DropColumn(
                name: "OverdueDailyFee",
                table: "LockerPricingPolicies");

            migrationBuilder.DropColumn(
                name: "LockerRentalContractId",
                table: "LockerIncidentLogs");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "LockerIncidentLogs");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "LockerIncidentLogs");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_CustomerUserId",
                table: "LockerRentalContracts",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_LockerId",
                table: "LockerRentalContracts",
                column: "LockerId");
        }
    }
}
