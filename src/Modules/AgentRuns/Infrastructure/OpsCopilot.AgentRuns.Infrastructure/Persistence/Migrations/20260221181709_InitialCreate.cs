using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agentRuns");

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                schema: "agentRuns",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AlertFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "ToolCalls",
                schema: "agentRuns",
                columns: table => new
                {
                    ToolCallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolCalls", x => x.ToolCallId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_AlertFingerprint",
                schema: "agentRuns",
                table: "AgentRuns",
                column: "AlertFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_TenantId",
                schema: "agentRuns",
                table: "AgentRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_TenantId_CreatedAtUtc",
                schema: "agentRuns",
                table: "AgentRuns",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ExecutedAtUtc",
                schema: "agentRuns",
                table: "ToolCalls",
                column: "ExecutedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_RunId",
                schema: "agentRuns",
                table: "ToolCalls",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuns",
                schema: "agentRuns");

            migrationBuilder.DropTable(
                name: "ToolCalls",
                schema: "agentRuns");
        }
    }
}
