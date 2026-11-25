using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseItemsPricedAtCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Staff no longer estimate prices; the Manager enters the real unit price when the purchase is done.
            // Completed proposals keep their old price as the recorded one; open ones lose the estimate.
            migrationBuilder.RenameColumn(
                name: "EstimatedUnitPrice",
                table: "PurchaseProposalItems",
                newName: "UnitPrice");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "PurchaseProposalItems",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.Sql(
                "UPDATE PurchaseProposalItems SET UnitPrice = NULL WHERE PurchaseProposalId IN " +
                "(SELECT Id FROM PurchaseProposals WHERE ProposalStatus <> 'Completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE PurchaseProposalItems SET UnitPrice = 0 WHERE UnitPrice IS NULL");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "PurchaseProposalItems",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                table: "PurchaseProposalItems",
                newName: "EstimatedUnitPrice");
        }
    }
}
