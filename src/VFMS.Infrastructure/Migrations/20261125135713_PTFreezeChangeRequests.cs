using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <summary>Adds the Manager-approved withdraw / early-resume request columns to PTFreezes (columns only).</summary>
    public partial class PTFreezeChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeOutcome",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangePayloadJson",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeRequestStatus",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeRequestType",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangeRequestedAt",
                table: "PTFreezes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ChangeResumeDate",
                table: "PTFreezes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReviewNote",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangeReviewedAt",
                table: "PTFreezes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReviewedByUserId",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeOutcome",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangePayloadJson",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeRequestStatus",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeRequestType",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeRequestedAt",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeResumeDate",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeReviewNote",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeReviewedAt",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeReviewedByUserId",
                table: "PTFreezes");
        }
    }
}
