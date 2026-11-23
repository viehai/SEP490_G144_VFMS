using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LockerSpecV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentStartedAt",
                table: "PaymentTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutstandingPaidAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockerRules",
                table: "Gyms",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentStartedAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "OutstandingPaidAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "LockerRules",
                table: "Gyms");
        }
    }
}
