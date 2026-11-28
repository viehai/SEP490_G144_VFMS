using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseDefaultMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EquipmentId",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_EquipmentId",
                table: "Exercises",
                column: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_Equipments_EquipmentId",
                table: "Exercises",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_Equipments_EquipmentId",
                table: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_EquipmentId",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                table: "Exercises");
        }
    }
}
