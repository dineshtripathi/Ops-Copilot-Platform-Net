using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.SafeActions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionLogAuditDeny : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enforce audit immutability at the database level:
            // ExecutionLogs rows must never be updated or deleted after insertion.
            migrationBuilder.Sql(
                "DENY UPDATE, DELETE ON [safeActions].[ExecutionLogs] TO PUBLIC;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "REVOKE UPDATE, DELETE ON [safeActions].[ExecutionLogs] FROM PUBLIC;");
        }
    }
}
