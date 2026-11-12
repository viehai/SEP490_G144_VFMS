using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPurchaseSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PaymentTransactions] WHERE DATALENGTH([GatewayTransactionReference]) > 200)
                    THROW 51000, 'A gateway reference exceeds 100 characters. Review existing values before migrating.', 1;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayTransactionReference",
                table: "PaymentTransactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            // Earlier checkout builds already created this column in some databases.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.OrderItems', N'ComponentTermsSnapshot') IS NULL
                    ALTER TABLE [dbo].[OrderItems] ADD [ComponentTermsSnapshot] nvarchar(max) NULL;
                ELSE IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.OrderItems') AND name = N'ComponentTermsSnapshot'
                        AND system_type_id = TYPE_ID(N'nvarchar') AND max_length = -1 AND is_nullable = 1
                )
                    THROW 51001, 'The existing ComponentTermsSnapshot column must be nullable nvarchar(max).', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_GatewayTransactionReference",
                table: "PaymentTransactions",
                column: "GatewayTransactionReference",
                unique: true,
                filter: "[GatewayName] = 'SePay' AND [GatewayTransactionReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_GatewayTransactionReference",
                table: "PaymentTransactions");

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20261108135332_AddMembershipPurchaseCheckout'
                )
                    ALTER TABLE [dbo].[OrderItems] DROP COLUMN [ComponentTermsSnapshot];
                """);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayTransactionReference",
                table: "PaymentTransactions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
