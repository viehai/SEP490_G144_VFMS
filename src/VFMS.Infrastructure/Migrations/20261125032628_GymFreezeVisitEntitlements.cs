using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GymFreezeVisitEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinFreezeDaysPerRequest",
                table: "PackageComponents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedFreezeCount",
                table: "MemberMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedMaxFreezeDays",
                table: "MemberMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedMinFreezeDays",
                table: "MemberMemberships",
                type: "int",
                nullable: true);
            // The former UI never configured a count. Preserve enabled defaults and explicit opt-outs for new sales.
            migrationBuilder.Sql("UPDATE FreezePolicies SET DefaultMaxFreezeCount = 1 WHERE DefaultMaxFreezeCount = 0 AND DefaultFreezeAllowanceDays > 0 AND IsGlobal = 1");
            migrationBuilder.Sql("""
                UPDATE PackageComponents SET MaxFreezeDaysPerRequest =
                    (SELECT FreezeAllowanceOverrideDays FROM Packages WHERE Packages.Id = PackageComponents.PackageId)
                WHERE ComponentType = 'GYM' AND MaxFreezeDaysPerRequest IS NULL AND PackageId IN
                    (SELECT Id FROM Packages WHERE FreezeAllowanceOverrideDays > 0)
                """);
            migrationBuilder.Sql("UPDATE PackageComponents SET MaxFreezeCount = 0 WHERE ComponentType = 'GYM' AND MaxFreezeCount IS NULL AND PackageId IN (SELECT Id FROM Packages WHERE FreezeAllowanceOverrideDays = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinFreezeDaysPerRequest",
                table: "PackageComponents");

            migrationBuilder.DropColumn(
                name: "GrantedFreezeCount",
                table: "MemberMemberships");

            migrationBuilder.DropColumn(
                name: "GrantedMaxFreezeDays",
                table: "MemberMemberships");

            migrationBuilder.DropColumn(
                name: "GrantedMinFreezeDays",
                table: "MemberMemberships");
        }
    }
}
