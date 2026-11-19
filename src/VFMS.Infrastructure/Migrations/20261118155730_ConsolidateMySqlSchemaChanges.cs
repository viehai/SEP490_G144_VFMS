using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateMySqlSchemaChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsultationConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerUserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    ManagerUserId = table.Column<string>(type: "nvarchar(128)", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsultationConversations_ApplicationUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsultationConversations_ApplicationUsers_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsultationMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsultationMessages_ApplicationUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsultationMessages_ConsultationConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ConsultationConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationConversations_CustomerUserId",
                table: "ConsultationConversations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationConversations_ManagerUserId",
                table: "ConsultationConversations",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationMessages_ConversationId",
                table: "ConsultationMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationMessages_SenderUserId",
                table: "ConsultationMessages",
                column: "SenderUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultationMessages");

            migrationBuilder.DropTable(
                name: "ConsultationConversations");
        }
    }
}
