using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VFMS.Infrastructure.MySql.Migrations;

[DbContext(typeof(VfmsMySqlDbContext))]
[Migration("20261116100300_AddPlanItemEquipmentAndWorkoutLogs")]
public sealed class AddPlanItemEquipmentAndWorkoutLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "EquipmentId",
            table: "PlanItems",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlanItems_EquipmentId",
            table: "PlanItems",
            column: "EquipmentId");

        migrationBuilder.AddForeignKey(
            name: "FK_PlanItems_Equipments_EquipmentId",
            table: "PlanItems",
            column: "EquipmentId",
            principalTable: "Equipments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "WorkoutLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                PlanItemId = table.Column<int>(type: "int", nullable: false),
                LoggedOn = table.Column<DateOnly>(type: "date", nullable: false),
                SetsDone = table.Column<int>(type: "int", nullable: true),
                RepsDone = table.Column<int>(type: "int", nullable: true),
                WeightKg = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                DurationMinutes = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
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
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_WorkoutLogs_PlanItemId_LoggedOn",
            table: "WorkoutLogs",
            columns: new[] { "PlanItemId", "LoggedOn" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WorkoutLogs");

        migrationBuilder.DropForeignKey(
            name: "FK_PlanItems_Equipments_EquipmentId",
            table: "PlanItems");

        migrationBuilder.DropIndex(
            name: "IX_PlanItems_EquipmentId",
            table: "PlanItems");

        migrationBuilder.DropColumn(
            name: "EquipmentId",
            table: "PlanItems");
    }
}
