using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerInactivityAndDisputeReviewer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "NotAcceptingCountFrom",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainerAbsenceReviewedByUserId",
                table: "PTSessions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotAcceptingCountFrom",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "TrainerAbsenceReviewedByUserId",
                table: "PTSessions");
        }
    }
}
