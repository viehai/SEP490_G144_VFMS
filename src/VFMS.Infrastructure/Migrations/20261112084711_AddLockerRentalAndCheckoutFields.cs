using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLockerRentalAndCheckoutFields : Migration
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
                name: "HoldExpiresAt",
                table: "PaymentTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFee",
                table: "PaymentTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockerPurpose",
                table: "PaymentTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockerRentalContractId",
                table: "PaymentTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverdueFees",
                table: "PaymentTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentExpiresAt",
                table: "PaymentTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "PaymentTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundMethod",
                table: "PaymentTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                table: "PaymentTransactions",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "PaymentTransactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundResolvedAt",
                table: "PaymentTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundStatus",
                table: "PaymentTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundedByUserId",
                table: "PaymentTransactions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalMonths",
                table: "PaymentTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RentalExpiryAtQuote",
                table: "PaymentTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourcePaymentTransactionId",
                table: "PaymentTransactions",
                type: "int",
                nullable: true);

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

            migrationBuilder.AddColumn<DateTime>(
                name: "KeyLossConfirmedAt",
                table: "LockerRentalContracts",
                type: "datetime2",
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

            migrationBuilder.AddColumn<int>(
                name: "ReplacementLockerId",
                table: "LockerRentalContracts",
                type: "int",
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

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_LockerRentalContractId_LockerPurpose",
                table: "PaymentTransactions",
                columns: new[] { "LockerRentalContractId", "LockerPurpose" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_SourcePaymentTransactionId",
                table: "PaymentTransactions",
                column: "SourcePaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_CustomerUserId_ContractStatus",
                table: "LockerRentalContracts",
                columns: new[] { "CustomerUserId", "ContractStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_LockerId_ContractStatus",
                table: "LockerRentalContracts",
                columns: new[] { "LockerId", "ContractStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_ReplacementLockerId",
                table: "LockerRentalContracts",
                column: "ReplacementLockerId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerIncidentLogs_LockerRentalContractId",
                table: "LockerIncidentLogs",
                column: "LockerRentalContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_LockerIncidentLogs_LockerRentalContracts_LockerRentalContractId",
                table: "LockerIncidentLogs",
                column: "LockerRentalContractId",
                principalTable: "LockerRentalContracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LockerRentalContracts_Lockers_ReplacementLockerId",
                table: "LockerRentalContracts",
                column: "ReplacementLockerId",
                principalTable: "Lockers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentTransactions_LockerRentalContracts_LockerRentalContractId",
                table: "PaymentTransactions",
                column: "LockerRentalContractId",
                principalTable: "LockerRentalContracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentTransactions_PaymentTransactions_SourcePaymentTransactionId",
                table: "PaymentTransactions",
                column: "SourcePaymentTransactionId",
                principalTable: "PaymentTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE purchase
                SET HoldExpiresAt = DATEADD(minute, 20, orders.CreatedAt)
                FROM PTPurchases AS purchase
                JOIN Orders AS orders ON orders.Id = purchase.OrderId
                WHERE purchase.Status = 'PendingPayment' AND orders.OrderStatus = 'Pending'
                    AND purchase.HoldExpiresAt = DATEADD(minute, 15, orders.CreatedAt);

                EXEC(N'
                    UPDATE payment
                    SET RefundAmount = payment.AmountReceived, RefundStatus = ''Pending'', RefundReason = ''CheckoutUnfulfilled''
                    FROM PaymentTransactions AS payment
                    JOIN PTPurchases AS purchase ON purchase.OrderId = payment.OrderId
                    WHERE payment.RawGatewayStatus = ''PT_RECONCILIATION_REQUIRED''
                        AND payment.PaymentStatus = ''Cancelled'' AND payment.GatewayName = ''SePay''
                        AND payment.GatewayTransactionReference IS NOT NULL AND payment.AmountReceived > 0
                        AND payment.PaidAt IS NOT NULL AND payment.RefundStatus IS NULL;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LockerIncidentLogs_LockerRentalContracts_LockerRentalContractId",
                table: "LockerIncidentLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_LockerRentalContracts_Lockers_ReplacementLockerId",
                table: "LockerRentalContracts");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentTransactions_LockerRentalContracts_LockerRentalContractId",
                table: "PaymentTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentTransactions_PaymentTransactions_SourcePaymentTransactionId",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_LockerRentalContractId_LockerPurpose",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_SourcePaymentTransactionId",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_CustomerUserId_ContractStatus",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_LockerId_ContractStatus",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerRentalContracts_ReplacementLockerId",
                table: "LockerRentalContracts");

            migrationBuilder.DropIndex(
                name: "IX_LockerIncidentLogs_LockerRentalContractId",
                table: "LockerIncidentLogs");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFee",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "LockerPurpose",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "LockerRentalContractId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "OverdueFees",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PaymentExpiresAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundMethod",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundResolvedAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundStatus",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundedByUserId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RenewalMonths",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RentalExpiryAtQuote",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "SourcePaymentTransactionId",
                table: "PaymentTransactions");

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
                name: "KeyLossConfirmedAt",
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
                name: "ReplacementLockerId",
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
