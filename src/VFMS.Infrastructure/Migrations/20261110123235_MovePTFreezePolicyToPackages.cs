using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MovePTFreezePolicyToPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FreezeMaxTotalDaysSnapshot",
                table: "PTPurchases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreezeMinimumDaysSnapshot",
                table: "PTPurchases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeMaxTotalDays",
                table: "PackageComponents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeMinimumDays",
                table: "PackageComponents",
                type: "int",
                nullable: true);

            // The package catalog is shared by the gym. Existing contracts used the trainer's gym policy.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE component
                SET [PTFreezeMinimumDays] = COALESCE(policy.[PTFreezeMinimumDays], 7),
                    [PTFreezeMaxTotalDays] = CASE component.[SessionCount]
                        WHEN 12 THEN COALESCE(policy.[PTFreezeCap12], 15)
                        WHEN 24 THEN COALESCE(policy.[PTFreezeCap24], 30)
                        WHEN 36 THEN COALESCE(policy.[PTFreezeCap36], 60)
                        ELSE 0 END
                FROM [PackageComponents] AS component
                OUTER APPLY (SELECT TOP (1) * FROM [Gyms] ORDER BY [IsDeleted], [Id]) AS policy
                WHERE component.[ComponentType] = N''PT'' AND component.[ApplicableTrainerLevel] IS NOT NULL;

                UPDATE purchase
                SET [FreezeMinimumDaysSnapshot] = gym.[PTFreezeMinimumDays],
                    [FreezeMaxTotalDaysSnapshot] = CASE purchase.[TotalSessions]
                        WHEN 12 THEN gym.[PTFreezeCap12]
                        WHEN 24 THEN gym.[PTFreezeCap24]
                        WHEN 36 THEN gym.[PTFreezeCap36]
                        ELSE 0 END
                FROM [PTPurchases] AS purchase
                JOIN [ApplicationUsers] AS trainer ON trainer.[UserId] = purchase.[PTUserId]
                JOIN [Gyms] AS gym ON gym.[Id] = trainer.[GymId];
                ');
                """);

            migrationBuilder.DropColumn(
                name: "PTFreezeCap12",
                table: "Gyms");
            migrationBuilder.DropColumn(
                name: "PTFreezeCap24",
                table: "Gyms");
            migrationBuilder.DropColumn(
                name: "PTFreezeCap36",
                table: "Gyms");
            migrationBuilder.DropColumn(
                name: "PTFreezeMinimumDays",
                table: "Gyms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "Per-package PT freeze policies and contract snapshots cannot be restored to one gym-wide policy without data loss.");
        }
    }
}
