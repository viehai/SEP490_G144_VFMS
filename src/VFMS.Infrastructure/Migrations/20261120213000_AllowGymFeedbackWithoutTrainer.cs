using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Infrastructure.Data;

#nullable disable

namespace VFMS.Infrastructure.Migrations;

[DbContext(typeof(VfmsDbContext))]
[Migration("20261120213000_AllowGymFeedbackWithoutTrainer")]
public partial class AllowGymFeedbackWithoutTrainer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TrainerUserId",
            table: "TrainerReviews",
            type: "nvarchar(128)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [TrainerReviews] WHERE [TrainerUserId] IS NULL)
                THROW 51000, 'Gym reviews without a trainer must be removed before reverting this migration.', 1;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "TrainerUserId",
            table: "TrainerReviews",
            type: "nvarchar(128)",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldNullable: true);
    }
}
