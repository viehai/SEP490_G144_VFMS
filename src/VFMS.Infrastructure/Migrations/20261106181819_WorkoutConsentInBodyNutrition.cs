using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutConsentInBodyNutrition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NutritionAdvice",
                table: "WorkoutPlans",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiConsentGrantedAt",
                table: "PhysicalProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BodyFatPercent",
                table: "PhysicalProfiles",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SkeletalMuscleMassKg",
                table: "PhysicalProfiles",
                type: "decimal(5,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NutritionAdvice",
                table: "WorkoutPlans");

            migrationBuilder.DropColumn(
                name: "AiConsentGrantedAt",
                table: "PhysicalProfiles");

            migrationBuilder.DropColumn(
                name: "BodyFatPercent",
                table: "PhysicalProfiles");

            migrationBuilder.DropColumn(
                name: "SkeletalMuscleMassKg",
                table: "PhysicalProfiles");
        }
    }
}
