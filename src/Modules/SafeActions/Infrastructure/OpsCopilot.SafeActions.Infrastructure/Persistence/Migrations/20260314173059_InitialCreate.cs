using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsCopilot.SafeActions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "safeActions");

            migrationBuilder.CreateTable(
                name: "ActionRecords",
                schema: "safeActions",
                columns: table => new
                {
                    ActionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProposedPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RollbackStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExecutionPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RollbackPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RollbackOutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManualRollbackGuidance = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RolledBackAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionRecords", x => x.ActionRecordId);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRecords",
                schema: "safeActions",
                columns: table => new
                {
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverIdentity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRecords", x => x.ApprovalId);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLogs",
                schema: "safeActions",
                columns: table => new
                {
                    ExecutionLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsePayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ExecutedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLogs", x => x.ExecutionLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionRecords_RunId",
                schema: "safeActions",
                table: "ActionRecords",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionRecords_TenantId",
                schema: "safeActions",
                table: "ActionRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionRecords_TenantId_CreatedAtUtc",
                schema: "safeActions",
                table: "ActionRecords",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionRecords_TenantId_Status",
                schema: "safeActions",
                table: "ActionRecords",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecords_ActionRecordId",
                schema: "safeActions",
                table: "ApprovalRecords",
                column: "ActionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecords_CreatedAtUtc",
                schema: "safeActions",
                table: "ApprovalRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_ActionRecordId",
                schema: "safeActions",
                table: "ExecutionLogs",
                column: "ActionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_ExecutedAtUtc",
                schema: "safeActions",
                table: "ExecutionLogs",
                column: "ExecutedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionRecords",
                schema: "safeActions");

            migrationBuilder.DropTable(
                name: "ApprovalRecords",
                schema: "safeActions");

            migrationBuilder.DropTable(
                name: "ExecutionLogs",
                schema: "safeActions");
        }
    }
}
