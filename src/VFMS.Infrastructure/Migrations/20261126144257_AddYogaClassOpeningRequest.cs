using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYogaClassOpeningRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YogaClassOpeningRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstructorUserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    YogaClassTypeId = table.Column<int>(type: "int", nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DifficultyLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SessionsPerWeek = table.Column<int>(type: "int", nullable: false),
                    MinParticipants = table.Column<int>(type: "int", nullable: false),
                    MaxParticipants = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreferredDays = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PreferredStartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    PreferredStudioId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(128)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    YogaClassId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaClassOpeningRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaClassOpeningRequests_ApplicationUsers_InstructorUserId",
                        column: x => x.InstructorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassOpeningRequests_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassOpeningRequests_YogaClassTypes_YogaClassTypeId",
                        column: x => x.YogaClassTypeId,
                        principalTable: "YogaClassTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassOpeningRequests_YogaClasses_YogaClassId",
                        column: x => x.YogaClassId,
                        principalTable: "YogaClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassOpeningRequests_YogaStudios_PreferredStudioId",
                        column: x => x.PreferredStudioId,
                        principalTable: "YogaStudios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassOpeningRequests_InstructorUserId_RequestStatus",
                table: "YogaClassOpeningRequests",
                columns: new[] { "InstructorUserId", "RequestStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassOpeningRequests_PreferredStudioId",
                table: "YogaClassOpeningRequests",
                column: "PreferredStudioId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassOpeningRequests_ReviewedByUserId",
                table: "YogaClassOpeningRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassOpeningRequests_YogaClassId",
                table: "YogaClassOpeningRequests",
                column: "YogaClassId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassOpeningRequests_YogaClassTypeId",
                table: "YogaClassOpeningRequests",
                column: "YogaClassTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YogaClassOpeningRequests");
        }
    }
}
