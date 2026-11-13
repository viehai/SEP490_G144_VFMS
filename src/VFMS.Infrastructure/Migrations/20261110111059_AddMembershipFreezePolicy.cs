using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipFreezePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinFreezeDaysPerRequest",
                table: "FreezePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FreezePolicyTierQuotas",
                columns: table => new
                {
                    Duration = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FreezePolicyId = table.Column<int>(type: "int", nullable: false),
                    MaximumCumulativeFreezeDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezePolicyTierQuotas", x => new { x.FreezePolicyId, x.Unit, x.Duration });
                    table.ForeignKey(
                        name: "FK_FreezePolicyTierQuotas_FreezePolicies_FreezePolicyId",
                        column: x => x.FreezePolicyId,
                        principalTable: "FreezePolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FreezePolicies_IsGlobal",
                table: "FreezePolicies",
                column: "IsGlobal",
                unique: true,
                filter: "[IsGlobal] = 1 AND [IsDeleted] = 0 AND [PolicyStatus] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FreezePolicyTierQuotas");

            migrationBuilder.DropIndex(
                name: "IX_FreezePolicies_IsGlobal",
                table: "FreezePolicies");

            migrationBuilder.DropColumn(
                name: "MinFreezeDaysPerRequest",
                table: "FreezePolicies");
        }
    }
}
