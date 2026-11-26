using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrainerInterviewTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InterviewedAt",
                table: "TrainerApplications",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterviewedAt",
                table: "TrainerApplications");
        }
    }
}
