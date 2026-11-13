using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutLogsAndPlanItemEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "PlanItems");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "PlanItems");

            migrationBuilder.AddColumn<int>(
                name: "EquipmentId",
                table: "PlanItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkoutLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanItemId = table.Column<int>(type: "int", nullable: false),
                    LoggedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SetsDone = table.Column<int>(type: "int", nullable: true),
                    RepsDone = table.Column<int>(type: "int", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_PlanItems_PlanItemId",
                        column: x => x.PlanItemId,
                        principalTable: "PlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanItems_EquipmentId",
                table: "PlanItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_PlanItemId_LoggedOn",
                table: "WorkoutLogs",
                columns: new[] { "PlanItemId", "LoggedOn" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanItems_Equipments_EquipmentId",
                table: "PlanItems",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlanItems_Equipments_EquipmentId",
                table: "PlanItems");

            migrationBuilder.DropTable(
                name: "WorkoutLogs");

            migrationBuilder.DropIndex(
                name: "IX_PlanItems_EquipmentId",
                table: "PlanItems");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                table: "PlanItems");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "PlanItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "PlanItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
