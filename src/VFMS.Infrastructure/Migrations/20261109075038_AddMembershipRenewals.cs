using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipRenewals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RenewedFromMembershipId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalAdvanceDays",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "RenewalGraceDays",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_RenewedFromMembershipId",
                table: "OrderItems",
                column: "RenewedFromMembershipId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MemberMemberships_RenewedFromMembershipId",
                table: "OrderItems",
                column: "RenewedFromMembershipId",
                principalTable: "MemberMemberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MemberMemberships_RenewedFromMembershipId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_RenewedFromMembershipId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RenewedFromMembershipId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RenewalAdvanceDays",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "RenewalGraceDays",
                table: "Gyms");
        }
    }
}
