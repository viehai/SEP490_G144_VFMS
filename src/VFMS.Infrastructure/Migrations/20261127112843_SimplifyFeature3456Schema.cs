using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyFeature3456Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Feature3456DataMigration.LockSources);
            migrationBuilder.AddColumn<string>(
                name: "ChangeState",
                table: "PTFreezes",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");
            migrationBuilder.Sql(Feature3456DataMigration.Consolidate);

            migrationBuilder.DropForeignKey(
                name: "FK_Lockers_LockerPricingPolicies_LockerPricingPolicyId",
                table: "Lockers");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentTransactions_ApplicationUsers_UserId",
                table: "PaymentTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_PTAvailabilities_TrainerProfiles_TrainerProfileId",
                table: "PTAvailabilities");

            migrationBuilder.DropForeignKey(
                name: "FK_PTFreezes_ApplicationUsers_RequestedByUserId",
                table: "PTFreezes");

            migrationBuilder.DropForeignKey(
                name: "FK_PTPurchases_TrainerProfiles_TrainerProfileId",
                table: "PTPurchases");

            migrationBuilder.DropForeignKey(
                name: "FK_PTSessions_ApplicationUsers_MemberUserId",
                table: "PTSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainerReviews_TrainerProfiles_TrainerProfileId",
                table: "TrainerReviews");

            migrationBuilder.DropIndex(
                name: "IX_TrainerReviews_TrainerProfileId",
                table: "TrainerReviews");

            migrationBuilder.DropIndex(
                name: "IX_PTSessions_MemberUserId_SessionDate_StartTime",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTSessions_PTPurchaseId",
                table: "PTSessions");

            migrationBuilder.DropIndex(
                name: "IX_PTPurchases_TrainerProfileId",
                table: "PTPurchases");

            migrationBuilder.DropIndex(
                name: "IX_PTFreezes_RequestedByUserId",
                table: "PTFreezes");

            migrationBuilder.DropIndex(
                name: "IX_PTAvailabilities_TrainerProfileId",
                table: "PTAvailabilities");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_UserId",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Lockers_LockerPricingPolicyId",
                table: "Lockers");

            migrationBuilder.DropColumn(
                name: "TrainerProfileId",
                table: "TrainerReviews");

            migrationBuilder.DropColumn(
                name: "AcceptingSince",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "CertificateUrl",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "CommissionTier",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ApplicationFileUrl",
                table: "TrainerApplications");

            migrationBuilder.DropColumn(
                name: "AttendanceReminderSentAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "CheckInCompletedAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "MemberUserId",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposalEmailRetryAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposalEmailSentAt",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedAdjustmentType",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ProposedEndTime",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "PackageNameSnapshot",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "PurchaseDate",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ReceiptEmailRetryAt",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ReceiptEmailSentAt",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "RemainingSessions",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "TotalSessions",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "TrainerChangeDeadline",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "TrainerProfileId",
                table: "PTPurchases");

            migrationBuilder.DropColumn(
                name: "ActualStartDate",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeRequestStatus",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeRequestType",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "ChangeResumeDate",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "MaxDaysSnapshot",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "MinimumDaysSnapshot",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentUrl",
                table: "PTFreezes");

            migrationBuilder.DropColumn(
                name: "AvailableDate",
                table: "PTAvailabilities");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "PTAvailabilities");

            migrationBuilder.DropColumn(
                name: "TrainerProfileId",
                table: "PTAvailabilities");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFee",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PTFreezeMaxTotalDays",
                table: "PackageComponents");

            migrationBuilder.DropColumn(
                name: "PTFreezeMinimumDays",
                table: "PackageComponents");

            migrationBuilder.DropColumn(
                name: "AssignedPinCode",
                table: "Lockers");

            migrationBuilder.DropColumn(
                name: "KeyOrPinType",
                table: "Lockers");

            migrationBuilder.DropColumn(
                name: "LockerPricingPolicyId",
                table: "Lockers");

            migrationBuilder.DropColumn(
                name: "LockerType",
                table: "Lockers");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "FinalNoticeEmailSentAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "FinalNoticeRetryAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFeeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "OverdueDailyFeeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "RentalPrice",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "RentalTypeSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "TermMonthsSnapshot",
                table: "LockerRentalContracts");

            migrationBuilder.DropColumn(
                name: "KeyReplacementFee",
                table: "LockerPricingPolicies");

            migrationBuilder.DropColumn(
                name: "RentalType",
                table: "LockerPricingPolicies");

            migrationBuilder.RenameColumn(
                name: "NotAcceptingSince",
                table: "TrainerProfiles",
                newName: "ReadinessSince");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTPurchaseId_SessionDate_StartTime",
                table: "PTSessions",
                columns: new[] { "PTPurchaseId", "SessionDate", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Feature3456DataMigration.LockSources);
            migrationBuilder.Sql(Feature3456DataMigration.GuardDowngrade);
            migrationBuilder.DropIndex(
                name: "IX_PTSessions_PTPurchaseId_SessionDate_StartTime",
                table: "PTSessions");

            migrationBuilder.DropColumn(
                name: "ChangeState",
                table: "PTFreezes");

            migrationBuilder.RenameColumn(
                name: "ReadinessSince",
                table: "TrainerProfiles",
                newName: "NotAcceptingSince");

            migrationBuilder.AddColumn<int>(
                name: "TrainerProfileId",
                table: "TrainerReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptingSince",
                table: "TrainerProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateUrl",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommissionTier",
                table: "TrainerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "TrainerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationFileUrl",
                table: "TrainerApplications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceReminderSentAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInCompletedAt",
                table: "PTSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberUserId",
                table: "PTSessions",
                type: "nvarchar(128)",
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.AddColumn<string>(
                name: "ProposedAdjustmentType",
                table: "PTSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ProposedEndTime",
                table: "PTSessions",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "PTPurchases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PackageNameSnapshot",
                table: "PTPurchases",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PurchaseDate",
                table: "PTPurchases",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                table: "PTPurchases",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

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

            migrationBuilder.AddColumn<int>(
                name: "RemainingSessions",
                table: "PTPurchases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalSessions",
                table: "PTPurchases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrainerChangeDeadline",
                table: "PTPurchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrainerProfileId",
                table: "PTPurchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ActualStartDate",
                table: "PTFreezes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeRequestStatus",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeRequestType",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ChangeResumeDate",
                table: "PTFreezes",
                type: "date",
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
                name: "RequestedByUserId",
                table: "PTFreezes",
                type: "nvarchar(128)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentUrl",
                table: "PTFreezes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AvailableDate",
                table: "PTAvailabilities",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "PTAvailabilities",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "TrainerProfileId",
                table: "PTAvailabilities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFee",
                table: "PaymentTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "PaymentTransactions",
                type: "nvarchar(128)",
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.AddColumn<string>(
                name: "AssignedPinCode",
                table: "Lockers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyOrPinType",
                table: "Lockers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockerPricingPolicyId",
                table: "Lockers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockerType",
                table: "Lockers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalNoticeEmailSentAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalNoticeRetryAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFeeSnapshot",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "LockerRentalContracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverdueDailyFeeSnapshot",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RentalPrice",
                table: "LockerRentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RentalTypeSnapshot",
                table: "LockerRentalContracts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAt",
                table: "LockerRentalContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermMonthsSnapshot",
                table: "LockerRentalContracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KeyReplacementFee",
                table: "LockerPricingPolicies",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RentalType",
                table: "LockerPricingPolicies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_TrainerProfileId",
                table: "TrainerReviews",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_MemberUserId_SessionDate_StartTime",
                table: "PTSessions",
                columns: new[] { "MemberUserId", "SessionDate", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTPurchaseId",
                table: "PTSessions",
                column: "PTPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_TrainerProfileId",
                table: "PTPurchases",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PTFreezes_RequestedByUserId",
                table: "PTFreezes",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTAvailabilities_TrainerProfileId",
                table: "PTAvailabilities",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_UserId",
                table: "PaymentTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Lockers_LockerPricingPolicyId",
                table: "Lockers",
                column: "LockerPricingPolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lockers_LockerPricingPolicies_LockerPricingPolicyId",
                table: "Lockers",
                column: "LockerPricingPolicyId",
                principalTable: "LockerPricingPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentTransactions_ApplicationUsers_UserId",
                table: "PaymentTransactions",
                column: "UserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PTAvailabilities_TrainerProfiles_TrainerProfileId",
                table: "PTAvailabilities",
                column: "TrainerProfileId",
                principalTable: "TrainerProfiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PTFreezes_ApplicationUsers_RequestedByUserId",
                table: "PTFreezes",
                column: "RequestedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PTPurchases_TrainerProfiles_TrainerProfileId",
                table: "PTPurchases",
                column: "TrainerProfileId",
                principalTable: "TrainerProfiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PTSessions_ApplicationUsers_MemberUserId",
                table: "PTSessions",
                column: "MemberUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerReviews_TrainerProfiles_TrainerProfileId",
                table: "TrainerReviews",
                column: "TrainerProfileId",
                principalTable: "TrainerProfiles",
                principalColumn: "Id");
        }
    }
}
