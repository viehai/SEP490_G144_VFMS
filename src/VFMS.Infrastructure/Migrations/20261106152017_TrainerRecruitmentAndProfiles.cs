using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrainerRecruitmentAndProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse narrowing legacy text rather than silently discarding existing content.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM TrainerProfiles WHERE DATALENGTH(CommissionTier) > 200 OR DATALENGTH(Bio) > 8000) OR EXISTS (SELECT 1 FROM TrainerApplications WHERE DATALENGTH(ReviewNote) > 4000) THROW 51002, 'Review legacy trainer text lengths before migration.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_ApplicantUserId",
                table: "TrainerApplications");

            migrationBuilder.AlterColumn<string>(
                name: "CommissionTier",
                table: "TrainerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "TrainerProfiles",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvatarFileId",
                table: "TrainerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceYears",
                table: "TrainerProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "TrainerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Specialty",
                table: "TrainerProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewNote",
                table: "TrainerApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "TrainerApplications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAt",
                table: "TrainerApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "TrainerApplications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceYears",
                table: "TrainerApplications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "TrainerApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HiredUserId",
                table: "TrainerApplications",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialty",
                table: "TrainerApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrackingCode",
                table: "TrainerApplications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE TrainerApplications SET TrackingCode = CONCAT('TR-', REPLACE(CONVERT(varchar(36), NEWID()), '-', '')) WHERE TrackingCode = ''; ");

            migrationBuilder.CreateTable(
                name: "TrainerCredentialChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrainerProfileId = table.Column<int>(type: "int", nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(128)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerCredentialChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerCredentialChange_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerCredentialChange_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainerEmail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerEmail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerRecruitmentRestriction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CitizenId = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerRecruitmentRestriction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerRecruitmentRestriction_ApplicationUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainerSetupToken",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerSetupToken", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerSetupToken_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainerDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrainerApplicationId = table.Column<int>(type: "int", nullable: true),
                    TrainerProfileId = table.Column<int>(type: "int", nullable: true),
                    TrainerCredentialChangeId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsAvatar = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerDocument_TrainerApplications_TrainerApplicationId",
                        column: x => x.TrainerApplicationId,
                        principalTable: "TrainerApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerDocument_TrainerCredentialChange_TrainerCredentialChangeId",
                        column: x => x.TrainerCredentialChangeId,
                        principalTable: "TrainerCredentialChange",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerDocument_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_ApplicantUserId",
                table: "TrainerApplications",
                column: "ApplicantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_CitizenId_SubmittedAt",
                table: "TrainerApplications",
                columns: new[] { "CitizenId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_Email_SubmittedAt",
                table: "TrainerApplications",
                columns: new[] { "Email", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_HiredUserId",
                table: "TrainerApplications",
                column: "HiredUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_TrackingCode",
                table: "TrainerApplications",
                column: "TrackingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerCredentialChange_ReviewedByUserId",
                table: "TrainerCredentialChange",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerCredentialChange_TrainerProfileId",
                table: "TrainerCredentialChange",
                column: "TrainerProfileId",
                unique: true,
                filter: "[Status] = 'Pending' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerDocument_TrainerApplicationId",
                table: "TrainerDocument",
                column: "TrainerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerDocument_TrainerCredentialChangeId",
                table: "TrainerDocument",
                column: "TrainerCredentialChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerDocument_TrainerProfileId",
                table: "TrainerDocument",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerEmail_SentAt_NextAttemptAt",
                table: "TrainerEmail",
                columns: new[] { "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerRecruitmentRestriction_ChangedByUserId",
                table: "TrainerRecruitmentRestriction",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerRecruitmentRestriction_CitizenId",
                table: "TrainerRecruitmentRestriction",
                column: "CitizenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerRecruitmentRestriction_Email",
                table: "TrainerRecruitmentRestriction",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerSetupToken_TokenHash",
                table: "TrainerSetupToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerSetupToken_UserId",
                table: "TrainerSetupToken",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerApplications_ApplicationUsers_HiredUserId",
                table: "TrainerApplications",
                column: "HiredUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainerApplications_ApplicationUsers_HiredUserId",
                table: "TrainerApplications");

            migrationBuilder.DropTable(
                name: "TrainerDocument");

            migrationBuilder.DropTable(
                name: "TrainerEmail");

            migrationBuilder.DropTable(
                name: "TrainerRecruitmentRestriction");

            migrationBuilder.DropTable(
                name: "TrainerSetupToken");

            migrationBuilder.DropTable(
                name: "TrainerCredentialChange");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_ApplicantUserId",
                table: "TrainerApplications");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_CitizenId_SubmittedAt",
                table: "TrainerApplications");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_Email_SubmittedAt",
                table: "TrainerApplications");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_HiredUserId",
                table: "TrainerApplications");

            migrationBuilder.DropIndex(
                name: "IX_TrainerApplications_TrackingCode",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "AvatarFileId",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ExperienceYears",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "Specialty",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "ConsentAt",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "ExperienceYears",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "HiredUserId",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "Specialty",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "TrackingCode",
                table: "TrainerApplications");

            migrationBuilder.AlterColumn<string>(
                name: "CommissionTier",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewNote",
                table: "TrainerApplications",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_ApplicantUserId",
                table: "TrainerApplications",
                column: "ApplicantUserId",
                unique: true,
                filter: "[ApplicantUserId] IS NOT NULL");
        }
    }
}
