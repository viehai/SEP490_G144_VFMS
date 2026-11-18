using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VFMS.Infrastructure.MySql.Migrations;

public partial class AddFeedbackConsultations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConsultationConversations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                CustomerUserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                ManagerUserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                LastMessageAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
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
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ConversationId = table.Column<int>(type: "int", nullable: false),
                SenderUserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Content = table.Column<string>(type: "longtext", nullable: false),
                IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConsultationMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConsultationMessages_ConsultationConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "ConsultationConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ConsultationMessages_ApplicationUsers_SenderUserId",
                    column: x => x.SenderUserId,
                    principalTable: "ApplicationUsers",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Restrict);
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConsultationMessages");
        migrationBuilder.DropTable(name: "ConsultationConversations");
    }
}
