using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "agentRuns",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TenantId_ExpiresAtUtc",
                schema: "agentRuns",
                table: "Sessions",
                columns: new[] { "TenantId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "agentRuns");
        }
    }
}
