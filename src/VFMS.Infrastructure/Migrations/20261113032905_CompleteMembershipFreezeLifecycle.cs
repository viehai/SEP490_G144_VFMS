using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMembershipFreezeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SupportingDocumentContent",
                table: "MembershipFreezes",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentContentType",
                table: "MembershipFreezes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentFileName",
                table: "MembershipFreezes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupportingDocumentId",
                table: "MembershipFreezes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id",
                table: "MembershipFreezes",
                columns: new[] { "RequestStatus", "RequestedEndDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id",
                table: "MembershipFreezes",
                columns: new[] { "RequestStatus", "RequestedStartDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id",
                table: "MembershipFreezes");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentContent",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentContentType",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentFileName",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentId",
                table: "MembershipFreezes");
        }
    }
}
