using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImplementPersonalTrainingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PTSessions_MemberUserId",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTSessions_PTUserId",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTAvailabilities_PTUserId",
                table: "PTAvailabilities");

            migrationBuilder.AddColumn<int>(
                name: "PTPurchaseId",
                table: "TrainerReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PTAvailabilityId",
                table: "PTSessions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CreditUnits",
                table: "PTSessions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsCreditForfeited",
                table: "PTSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NoteUpdatedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposalEmailRetryAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposalEmailSentAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposalExpiresAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedAdjustmentType",
                table: "PTSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ProposedDate",
                table: "PTSessions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ProposedEndTime",
                table: "PTSessions",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ProposedStartTime",
                table: "PTSessions",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacementForSessionId",
                table: "PTSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresAt",
                table: "PTPurchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "PTPurchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageNameSnapshot",
                table: "PTPurchases",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptEmailRetryAt",
                table: "PTPurchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptEmailSentAt",
                table: "PTPurchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PTPurchases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.Sql("UPDATE [PTPurchases] SET [Status] = 'Completed' WHERE [IsCompleted] = 1;");

            migrationBuilder.AddColumn<int>(
                name: "TrainerLevelSnapshot",
                table: "PTPurchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyPatternJson",
                table: "PTPurchases",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysSnapshot",
                table: "PTFreezes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumDaysSnapshot",
                table: "PTFreezes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentUrl",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "AvailableDate",
                table: "PTAvailabilities",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                table: "PTAvailabilities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApplicableTrainerLevel",
                table: "PackageComponents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeCap12",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeCap24",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeCap36",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "PTFreezeMinimumDays",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_PTPurchaseId",
                table: "TrainerReviews",
                column: "PTPurchaseId",
                unique: true,
                filter: "[PTPurchaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_MemberUserId_SessionDate_StartTime",
                table: "PTSessions",
                columns: new[] { "MemberUserId", "SessionDate", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTUserId_SessionDate_StartTime",
                table: "PTSessions",
                columns: new[] { "PTUserId", "SessionDate", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_OrderId",
                table: "PTPurchases",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PTAvailabilities_PTUserId_DayOfWeek_StartTime",
                table: "PTAvailabilities",
                columns: new[] { "PTUserId", "DayOfWeek", "StartTime" },
                unique: true,
                filter: "[DayOfWeek] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_PTPurchases_Orders_OrderId",
                table: "PTPurchases",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerReviews_PTPurchases_PTPurchaseId",
                table: "TrainerReviews",
                column: "PTPurchaseId",
                principalTable: "PTPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PTAvailabilities] WHERE [DayOfWeek] IS NOT NULL OR [AvailableDate] IS NULL)
                    OR EXISTS (SELECT 1 FROM [PTPurchases] WHERE [OrderId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [PTSessions] WHERE [PTAvailabilityId] IS NULL OR [ProposedDate] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [TrainerReviews] WHERE [PTPurchaseId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [PackageComponents] WHERE [ApplicableTrainerLevel] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [PTFreezes] WHERE [MinimumDaysSnapshot] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [Gyms] WHERE [PTFreezeMinimumDays] <> 7 OR [PTFreezeCap12] <> 15 OR [PTFreezeCap24] <> 30 OR [PTFreezeCap36] <> 60)
                    THROW 51000, 'Feature 4 contains data. Use a backup or a forward migration.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PTPurchases_Orders_OrderId",
                table: "PTPurchases");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainerReviews_PTPurchases_PTPurchaseId",
                table: "TrainerReviews");

            migrationBuilder.DropIndex(
                name: "IX_TrainerReviews_PTPurchaseId",
                table: "TrainerReviews");

            migrationBuilder.DropIndex(
                name: "IX_PTSessions_MemberUserId_SessionDate_StartTime",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTSessions_PTUserId_SessionDate_StartTime",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTPurchases_OrderId",
                table: "PTPurchases");

            migrationBuilder.DropIndex(
                name: "IX_PTAvailabilities_PTUserId_DayOfWeek_StartTime",
                table: "PTAvailabilities");

            migrationBuilder.DropColumn(
                name: "PTPurchaseId",
                table: "TrainerReviews");

            migrationBuilder.DropColumn(
                name: "CreditUnits",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "IsCreditForfeited",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "NoteUpdatedAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposalEmailRetryAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposalEmailSentAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposalExpiresAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedAdjustmentType",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedDate",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedEndTime",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedStartTime",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ReplacementForSessionId",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAt",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "PackageNameSnapshot",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ReceiptEmailRetryAt",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ReceiptEmailSentAt",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "TrainerLevelSnapshot",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "WeeklyPatternJson",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "MaxDaysSnapshot",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "MinimumDaysSnapshot",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentUrl",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                table: "PTAvailabilities");

            migrationBuilder.DropColumn(
                name: "ApplicableTrainerLevel",
                table: "PackageComponents");

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

            migrationBuilder.AlterColumn<int>(
                name: "PTAvailabilityId",
                table: "PTSessions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "AvailableDate",
                table: "PTAvailabilities",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_MemberUserId",
                table: "PTSessions",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTUserId",
                table: "PTSessions",
                column: "PTUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTAvailabilities_PTUserId",
                table: "PTAvailabilities",
                column: "PTUserId");
        }
    }
}
