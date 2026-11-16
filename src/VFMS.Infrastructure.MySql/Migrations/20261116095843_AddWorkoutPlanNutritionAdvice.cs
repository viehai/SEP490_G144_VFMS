using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VFMS.Infrastructure.MySql.Migrations;

[DbContext(typeof(VfmsMySqlDbContext))]
[Migration("20261116095843_AddWorkoutPlanNutritionAdvice")]
public sealed class AddWorkoutPlanNutritionAdvice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
                name: "NutritionAdvice",
                table: "WorkoutPlans",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NutritionAdvice",
            table: "WorkoutPlans");
    }
}
