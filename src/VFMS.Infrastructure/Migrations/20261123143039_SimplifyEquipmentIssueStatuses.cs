using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyEquipmentIssueStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Issues now have three statuses: InProgress (Staff), Escalated (Manager) and Resolved.
            // A Manager repair used to be "InProgress" with a Manager as handler; it is the Manager's again.
            migrationBuilder.Sql(
                "UPDATE EquipmentIssueReports SET IssueStatus = 'Escalated' WHERE IssueStatus = 'InProgress' AND HandledByUserId IN " +
                "(SELECT ur.UserId FROM ApplicationUserRoles ur INNER JOIN ApplicationRoles r ON r.Id = ur.RoleId WHERE r.RoleName = 'Manager')");
            migrationBuilder.Sql("UPDATE EquipmentIssueReports SET IssueStatus = 'InProgress' WHERE IssueStatus = 'Reported'");
            migrationBuilder.Sql("UPDATE EquipmentIssueReports SET IssueStatus = 'Resolved' WHERE IssueStatus = 'MarkedSafe'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old statuses cannot be told apart again; the new ones are all valid under the old code.
        }
    }
}
