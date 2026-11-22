using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrainerRoleId",
                table: "TrainerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrainerRoleId",
                table: "TrainerApplications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrainerRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerRoles_ApplicationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "ApplicationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerProfiles_TrainerRoleId",
                table: "TrainerProfiles",
                column: "TrainerRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_TrainerRoleId",
                table: "TrainerApplications",
                column: "TrainerRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerRoles_RoleId",
                table: "TrainerRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerRoles_RoleName",
                table: "TrainerRoles",
                column: "RoleName",
                unique: true);

            // Seed here, not in DbInitializer: it runs after migrations and the trainer rows need the
            // application roles to exist. DbInitializer then finds the roles already there and skips them.
            migrationBuilder.Sql("""
                INSERT INTO ApplicationRoles (RoleName, CreatedAt, UpdatedAt, IsDeleted)
                SELECT v.RoleName, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM (VALUES ('PT'), ('YogaInstructor')) v(RoleName)
                WHERE NOT EXISTS (SELECT 1 FROM ApplicationRoles r WHERE r.RoleName = v.RoleName);

                INSERT INTO TrainerRoles (RoleName, RoleId, IsActive, CreatedAt, UpdatedAt, IsDeleted)
                SELECT v.TrainerRole, r.Id, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM (VALUES ('PT', 'PT'), ('Yoga', 'YogaInstructor')) v(TrainerRole, AppRole)
                JOIN ApplicationRoles r ON r.RoleName = v.AppRole;
                """);

            // Existing rows: recruitment only hired PTs so far; a profile whose user is only a yoga instructor is Yoga.
            migrationBuilder.Sql("""
                UPDATE TrainerApplications SET TrainerRoleId = (SELECT Id FROM TrainerRoles WHERE RoleName = 'PT');

                UPDATE p SET TrainerRoleId = (SELECT Id FROM TrainerRoles WHERE RoleName =
                    CASE WHEN EXISTS (SELECT 1 FROM ApplicationUserRoles ur JOIN ApplicationRoles r ON r.Id = ur.RoleId
                                      WHERE ur.UserId = p.PTUserId AND ur.IsDeleted = 0 AND r.RoleName = 'YogaInstructor')
                          AND NOT EXISTS (SELECT 1 FROM ApplicationUserRoles ur JOIN ApplicationRoles r ON r.Id = ur.RoleId
                                          WHERE ur.UserId = p.PTUserId AND ur.IsDeleted = 0 AND r.RoleName = 'PT')
                         THEN 'Yoga' ELSE 'PT' END)
                FROM TrainerProfiles p;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerApplications_TrainerRoles_TrainerRoleId",
                table: "TrainerApplications",
                column: "TrainerRoleId",
                principalTable: "TrainerRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerProfiles_TrainerRoles_TrainerRoleId",
                table: "TrainerProfiles",
                column: "TrainerRoleId",
                principalTable: "TrainerRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainerApplications_TrainerRoles_TrainerRoleId",
                table: "TrainerApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainerProfiles_TrainerRoles_TrainerRoleId",
                table: "TrainerProfiles");

            migrationBuilder.DropTable(
                name: "TrainerRoles");

            migrationBuilder.DropIndex(
                name: "IX_TrainerProfiles_TrainerRoleId",
                table: "TrainerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_TrainerRoleId",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "TrainerRoleId",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "TrainerRoleId",
                table: "TrainerApplications");
        }
    }
}
